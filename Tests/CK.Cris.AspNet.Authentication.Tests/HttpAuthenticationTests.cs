using CK.Auth;
using CK.Core;
using CK.Cris;
using CK.Cris.AspNet;
using CK.Cris.AspNet.Authentication;
using CK.IO.Authentication;
using CK.IO.Authentication.Weak;
using CK.Poco.Exc.Json;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using AuthLevel = CK.Core.AuthLevel;

namespace CK.Cris.AspNet.Authentication.Tests;

/// <summary>
/// Not a session command: it goes to "/.cris". Answers the name of the (unsafe) actor.
/// </summary>
public interface IWhoAmIQCommand : ICommand<string>
{
}

public sealed class WhoAmIHandler : ISingletonAutoService
{
    [CommandHandler]
    public string Handle( IWhoAmIQCommand c, AuthenticationInfo info ) => info.UnsafeActor.IsAnonymous ? "Anonymous" : info.UnsafeActor.ActorName;
}

public sealed class NoActor : IActorInfoProvider
{
    public ValueTask<ActorInfo?> GetActorInfoAsync( IActivityMonitor monitor, int actorId ) => ValueTask.FromResult<ActorInfo?>( null );
}

/// <summary>
/// The fake authentication center: the tokens it issued, with the binding hash of their session.
/// </summary>
public sealed class FakeTokenReader : IAuthenticationTokenReader
{
    public static readonly ConcurrentDictionary<string, AuthenticationTokenData> Tokens = new();

    public ValueTask<AuthenticationTokenData?> ReadTokenAsync( IActivityMonitor monitor, string token )
        => ValueTask.FromResult( Tokens.TryGetValue( token, out var d ) ? d : null );
}

public sealed class FakeOpener : IAuthenticationSessionOpener
{
    readonly IAuthenticationSessionEndpoint _endpoint;

    public FakeOpener( IAuthenticationSessionEndpoint endpoint ) => _endpoint = endpoint;

    public ValueTask<ISessionResult> OpenSessionAsync<T>( IActivityMonitor monitor, T command, string scheme, object payload, bool rememberMe = false )
        where T : ICommand<ISessionResult>, ICommandAuthenticationSession
    {
        var name = ((IWeakLoginCommand)payload).WeakActorName;
        if( name != "Operator" )
        {
            return ValueTask.FromResult( command.CreateResult( r => r.Success = false ) );
        }
        return ValueTask.FromResult( IssueOperator( _endpoint, command, rememberMe ) );
    }

    /// <summary>
    /// Issues a session (new secrets and a new token) for the "Operator" weak actor.
    /// </summary>
    internal static ISessionResult IssueOperator( IAuthenticationSessionEndpoint endpoint, ICommand<ISessionResult> command, bool rememberMe )
    {
        var utcNow = DateTime.UtcNow;
        var proof = endpoint.Issue( "42.the-session", rememberMe, utcNow.AddHours( 1 ) );
        var actor = new ActorInfo( 42, "Operator", AuthLevel.Unsafe, [new SchemeInfo( ActorInfo.WeakScheme, utcNow )] );
        var info = AuthenticationInfo.Create( actor, utcNow.AddHours( 1 ), null, "D", utcNow );
        var token = "token-" + Guid.NewGuid().ToString( "N" );
        FakeTokenReader.Tokens[token] = new AuthenticationTokenData( info, proof.BindingSecretHash );
        return command.CreateResult( r =>
        {
            r.Info.InitializeFrom( info );
            r.Token = token;
            r.RememberMe = rememberMe;
        } );
    }
}

public sealed class FakeWeakProvider : ISingletonAutoService
{
    public static int Called;

    [CommandHandler]
    public ValueTask<ISessionResult> HandleAsync( IActivityMonitor monitor, IWeakLoginCommand cmd, IAuthenticationSessionOpener opener )
    {
        ++Called;
        return opener.OpenSessionAsync( monitor, cmd, ActorInfo.WeakScheme, cmd, rememberMe: true );
    }

    [CommandHandler]
    public void Handle( ILogoutSessionCommand cmd, IAuthenticationSessionEndpoint endpoint ) => endpoint.Clear();

    /// <summary>
    /// The refresh: a session is presented (the cookies) or it fails and the endpoint is cleared, like the AuthCenter does.
    /// </summary>
    [CommandHandler]
    public ISessionResult Handle( IRefreshSessionCommand cmd, IAuthenticationSessionEndpoint endpoint )
    {
        if( endpoint.PresentedProof == null )
        {
            endpoint.Clear();
            return cmd.CreateResult( r => r.Success = false );
        }
        return FakeOpener.IssueOperator( endpoint, cmd, rememberMe: true );
    }
}

/// <summary>
/// A relay that answers every session command (as a remote authentication center would).
/// </summary>
public sealed class FakeRelay : IAuthenticationSessionRelay
{
    public Task<object?>? TryRelay( IActivityMonitor monitor, ICommandAuthenticationSession command )
    {
        return command is IWeakLoginCommand login
                ? Task.FromResult<object?>( login.CreateResult( r => r.Token = "relayed" ) )
                : null;
    }
}

[TestFixture]
public class HttpAuthenticationTests
{
    internal static Task<RunningAspNetServer> StartServerAsync() => StartAsync();

    static async Task<RunningAspNetServer> StartAsync( bool withRelay = false, string? target = null, bool withOldStack = false )
    {
        target ??= TestHelper.GetTypeScriptInlineTargetProjectPath( "StartAsync" );
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( CrisAspNetService ),
                                              typeof( CrisAspNetAuthenticationPackage ),
                                              typeof( CrisAuthenticationService ),
                                              typeof( AuthenticationInfo ),
                                              typeof( AuthenticationInfoDefaultProvider ),
                                              typeof( NoActor ),
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( FakeTokenReader ),
                                              typeof( IAuthenticationSessionOpener ),
                                              typeof( FakeOpener ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              typeof( FakeWeakProvider ),
                                              typeof( WhoAmIHandler ),
                                              typeof( IWhoAmIQCommand ),
                                              typeof( IWeakLoginCommand ),
                                              typeof( ILogoutSessionCommand ),
                                              typeof( IRefreshSessionCommand ),
                                              typeof( IImpersonateCommand ),
                                              typeof( IClearImpersonationCommand ),
                                              typeof( ISessionResult ),
                                              typeof( IAuthenticationInfoPoco ),
                                              typeof( IActorInfoPoco ),
                                              typeof( CK.Cris.AmbientValues.AmbientValuesService ),
                                              typeof( IAuthAmbientValues ),
                                              typeof( ICultureAmbientValues ) );
        if( withRelay ) configuration.FirstBinPath.Types.Add( typeof( IAuthenticationSessionRelay ), typeof( FakeRelay ) );
        if( withOldStack ) configuration.FirstBinPath.Types.Add( typeof( CK.AspNet.Auth.AuthenticationInfoTokenService ), typeof( StdAuthenticationTypeSystem ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( target, typeof( IWhoAmIQCommand ), typeof( IWeakLoginCommand ), typeof( ILogoutSessionCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddCrisAuthentication();
        return await builder.CreateRunningAspNetServerAsync( map, app =>
        {
            app.UseCrisAuthentication();
            app.UseCris();
        } );
    }

    static string ToJson( RunningAspNetServer server, IPoco command )
    {
        var w = new ArrayBufferWriter<byte>();
        server.Services.GetRequiredService<PocoDirectory>()
              .WriteJson( w, command, withType: true, new PocoJsonExportOptions { TypeFilterName = "TypeScript", UseCamelCase = true } );
        return Encoding.UTF8.GetString( w.WrittenSpan );
    }

    static T Create<T>( RunningAspNetServer server, Action<T>? configure = null ) where T : class, IPoco
    {
        var p = server.Services.GetRequiredService<PocoDirectory>().Create<T>();
        configure?.Invoke( p );
        return p;
    }

    static async Task<ICrisCallResult> ReadResultAsync( RunningAspNetServer server, HttpResponseMessage r )
    {
        r.StatusCode.ShouldBe( HttpStatusCode.OK );
        return server.Services.GetRequiredService<PocoDirectory>().Find<ICrisCallResult>()!.ReadJson( await r.Content.ReadAsByteArrayAsync() ).ShouldNotBeNull();
    }

    static async Task<ICrisCallResult> PostAsync( RunningAspNetServer server, string path, IPoco command )
        => await ReadResultAsync( server, await server.Client.PostJsonAsync( path + "?UsePascalCase", ToJson( server, command ) ) );

    [Test]
    public async Task login_sets_the_cookies_and_the_bearer_needs_the_binding_cookie_Async()
    {
        await using var server = await StartAsync();
        var client = server.Client;

        (await PostAsync( server, "/.cris", Create<IWhoAmIQCommand>( server ) )).Result.ShouldBe( "Anonymous" );

        // Login on "/.cris/auth".
        var r = await client.PostJsonAsync( "/.cris/auth?UsePascalCase", ToJson( server, Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) ) );
        var setCookies = r.Headers.GetValues( "Set-Cookie" ).ToList();
        var binding = setCookies.Single( c => c.StartsWith( CrisAuthenticationCookies.BindingCookieName + "=" ) ).ToLowerInvariant();
        binding.ShouldContain( "path=/.cris;" );
        binding.ShouldContain( "httponly" );
        binding.ShouldContain( "samesite=strict" );
        binding.ShouldContain( "expires=", customMessage: "RememberMe: persistent cookies." );
        setCookies.Single( c => c.StartsWith( CrisAuthenticationCookies.RefreshCookieName + "=" ) ).ToLowerInvariant().ShouldContain( "path=/.cris/auth" );
        var login = (ISessionResult)(await ReadResultAsync( server, r )).Result.ShouldNotBeNull();
        login.Info.UnsafeActor.ActorName.ShouldBe( "Operator" );

        // The bearer token with the binding cookie: authenticated.
        client.Token = login.Token;
        (await PostAsync( server, "/.cris", Create<IWhoAmIQCommand>( server ) )).Result.ShouldBe( "Operator" );

        // The same token without the binding cookie (stolen token): anonymous.
        using( var thief = new HttpClient() { BaseAddress = new Uri( server.ServerAddress ) } )
        {
            thief.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", login.Token );
            var stolen = await thief.PostAsync( "/.cris?UsePascalCase", new StringContent( ToJson( server, Create<IWhoAmIQCommand>( server ) ), Encoding.UTF8, "application/json" ) );
            (await ReadResultAsync( server, stolen )).Result.ShouldBe( "Anonymous" );
        }

        // Logout clears the cookies: the token is useless.
        (await PostAsync( server, "/.cris/auth", Create<ILogoutSessionCommand>( server ) )).Result.ShouldBeNull();
        (await PostAsync( server, "/.cris", Create<IWhoAmIQCommand>( server ) )).Result.ShouldBe( "Anonymous" );
    }

    [Test]
    public async Task session_commands_go_to_cris_auth_and_only_them_Async()
    {
        await using var server = await StartAsync();
        // The "/.cris" routes write the user messages in their simple format: the response text is checked.
        async Task<string> PostTextAsync( string path, IPoco command )
            => await (await server.Client.PostJsonAsync( path, ToJson( server, command ) )).Content.ReadAsStringAsync();
        FakeWeakProvider.Called = 0;
        // The route is declared by [CrisRoute<ICommandAuthenticationSession>( "/.cris/auth" )] on the CrisAspNetAuthenticationPackage.
        (await PostTextAsync( "/.cris", Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) )).ShouldContain( "must be sent to \\u0027/.cris/auth\\u0027" );
        // The legacy "/.cris/net" accepts only the commands of "/.cris".
        (await PostTextAsync( "/.cris/net", Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) )).ShouldContain( "must be sent to \\u0027/.cris/auth\\u0027" );
        FakeWeakProvider.Called.ShouldBe( 0 );
        (await PostTextAsync( "/.cris/auth", Create<IWhoAmIQCommand>( server ) )).ShouldContain( "must be sent to \\u0027/.cris\\u0027" );
        // An undeclared route accepts no command.
        (await PostTextAsync( "/.cris/other", Create<IWhoAmIQCommand>( server ) )).ShouldContain( "must be sent to \\u0027/.cris\\u0027" );
        (await PostTextAsync( "/.cris", Create<IWhoAmIQCommand>( server ) )).ShouldNotContain( "must be sent to" );
    }

    [Test]
    public async Task the_old_stack_and_the_new_one_cannot_run_together_Async()
    {
        // The setup succeeds (CK.AspNet.Auth alone doesn't conflict with the new types): the application refuses to start.
        var ex = await Should.ThrowAsync<InvalidOperationException>( async () =>
        {
            await using var server = await StartAsync( withOldStack: true );
        } );
        ex.Message.ShouldContain( "old authentication stack (CK.AspNet.Auth)" );
    }

    /// <summary>
    /// Runs the TypeScript tests of the AuthService (src/*.spec.ts) against this server: the jsdom environment of the tests
    /// has the server's origin, so the cookies and the same-origin check work as in a browser.
    /// </summary>
    [Test]
    public async Task TypeScript_AuthService_Async()
    {
        var target = TestHelper.GetTypeScriptInlineTargetProjectPath();
        await using var server = await StartAsync( target: target );
        await using var runner = TestHelper.CreateTypeScriptRunner( target,
                                                                    server.ServerAddress,
                                                                    new System.Collections.Generic.Dictionary<string, string> { { "CRIS_ENDPOINT_URL", server.ServerAddress + "/.cris" } } );
        await TestHelper.SuspendAsync( resume => resume );
        runner.Run();
    }

    [Test]
    public async Task cris_requires_the_same_origin_and_cris_auth_a_json_POST_Async()
    {
        await using var server = await StartAsync();
        using var http = new HttpClient() { BaseAddress = new Uri( server.ServerAddress ) };
        var json = ToJson( server, Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) );

        async Task<HttpStatusCode> SendAsync( string? origin, string? fetchSite, string mediaType = "application/json", HttpMethod? method = null )
        {
            using var m = new HttpRequestMessage( method ?? HttpMethod.Post, "/.cris/auth" ) { Content = new StringContent( json, Encoding.UTF8, mediaType ) };
            if( origin != null ) m.Headers.Add( "Origin", origin );
            if( fetchSite != null ) m.Headers.Add( "Sec-Fetch-Site", fetchSite );
            return (await http.SendAsync( m )).StatusCode;
        }
        var self = server.ServerAddress.TrimEnd( '/' );
        (await SendAsync( "https://evil.example.com", null )).ShouldBe( HttpStatusCode.Forbidden );
        (await SendAsync( null, "same-site" )).ShouldBe( HttpStatusCode.Forbidden, "A sibling sub-domain." );
        (await SendAsync( null, "cross-site" )).ShouldBe( HttpStatusCode.Forbidden );
        (await SendAsync( self, null )).ShouldBe( HttpStatusCode.OK );
        (await SendAsync( null, "same-origin" )).ShouldBe( HttpStatusCode.OK );
        (await SendAsync( self, "same-origin", "text/plain" )).ShouldBe( HttpStatusCode.UnsupportedMediaType );
        (await SendAsync( self, "same-origin", method: HttpMethod.Put )).ShouldBe( HttpStatusCode.MethodNotAllowed );
    }

    [Test]
    public async Task a_relay_answers_the_session_commands_instead_of_the_handlers_Async()
    {
        await using var server = await StartAsync( withRelay: true );
        FakeWeakProvider.Called = 0;
        var r = await PostAsync( server, "/.cris/auth", Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) );
        ((ISessionResult)r.Result.ShouldNotBeNull()).Token.ShouldBe( "relayed" );
        FakeWeakProvider.Called.ShouldBe( 0, "Relayed: not executed here." );
    }
}

[TestFixture]
public class HttpFixesTests
{
    [Test]
    public async Task cris_requires_JSON_and_answers_JSON_without_sniffing_Async()
    {
        await using var server = await HttpAuthenticationTests.StartServerAsync();
        using var http = new HttpClient() { BaseAddress = new Uri( server.ServerAddress ) };
        var json = """["CK.Cris.AspNet.Authentication.Tests.IWhoAmIQCommand",{}]""";

        var plain = await http.PostAsync( "/.cris", new StringContent( json, Encoding.UTF8, "text/plain" ) );
        plain.StatusCode.ShouldBe( HttpStatusCode.UnsupportedMediaType, "A text/plain POST is sent cross-site without preflight." );

        var ok = await http.PostAsync( "/.cris", new StringContent( json, Encoding.UTF8, "application/json" ) );
        ok.StatusCode.ShouldBe( HttpStatusCode.OK );
        ok.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe( "application/json" );
        ok.Headers.GetValues( "X-Content-Type-Options" ).ShouldBe( ["nosniff"] );
    }

    [Test]
    public async Task a_request_body_that_cannot_be_read_is_never_logged_Async()
    {
        await using var server = await HttpAuthenticationTests.StartServerAsync();
        using var http = new HttpClient() { BaseAddress = new Uri( server.ServerAddress ) };
        using var logs = CK.Monitoring.GrandOutput.Default.ShouldNotBeNull().CreateMemoryCollector( 1000 );
        // A broken login: the password must not appear in the logs.
        var broken = """["CK.IO.Authentication.Weak.IWeakLoginCommand",{"weakActorName":"the-secret-value-3712",""";
        var r = await http.PostAsync( "/.cris/auth", new StringContent( broken, Encoding.UTF8, "application/json" ) );
        r.StatusCode.ShouldBe( HttpStatusCode.OK );
        (await r.Content.ReadAsStringAsync()).ShouldContain( "Unable to read Command Poco from request body" );
        await Task.Delay( 200 );
        var texts = logs.ExtractCurrentTexts();
        texts.ShouldContain( t => t.Contains( "Unable to read Command Poco from request body" ), "The error is logged..." );
        texts.ShouldNotContain( t => t.Contains( "the-secret-value-3712" ), "...but never the body." );
    }
}
