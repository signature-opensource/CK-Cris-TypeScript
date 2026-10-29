using CK.Auth;
using CK.Core;
using CK.Cris;
using CK.Cris.AspNet;
using CK.Cris.AspNet.Authentication;
using CK.IO.Authentication;
using CK.IO.Authentication.Basic;
using CK.IO.Authentication.Weak;
using CK.Poco.Exc.Json;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Buffers;
using System.Collections.Generic;
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

/// <summary>
/// Answers the CallerPartyId of the request.
/// </summary>
public interface IWhoCallsQCommand : ICommand<int>
{
}

public sealed class WhoAmIHandler : ISingletonAutoService
{
    [CommandHandler]
    public int Handle( IWhoCallsQCommand c, AuthenticationInfo info ) => info.CallerPartyId;

    [CommandHandler]
    public string Handle( IWhoAmIQCommand c, AuthenticationInfo info ) => info.UnsafeActor.IsAnonymous ? "Anonymous" : info.UnsafeActor.ActorName;
}

/// <summary>
/// An unsafe command: the <see cref="CrisAuthenticationService"/> checks that its <see cref="IAuthUnsafePart.ActorId"/>
/// is the one of the current authentication, otherwise the command is not validated.
/// </summary>
public interface IUnsafeCommand : ICommand, ICommandAuthUnsafe
{
    string UserInfo { get; set; }
}

public sealed class UnsafeHandler : ISingletonAutoService
{
    public static string? LastUserInfo;

    [CommandHandler]
    public void Execute( IUnsafeCommand cmd ) => LastUserInfo = cmd.UserInfo;
}

[TestFixture]
public class HttpAuthenticationTests
{
    internal static Task<RunningAspNetServer> StartServerAsync() => StartAsync();

    static async Task<RunningAspNetServer> StartAsync( string? target = null, bool withOldStack = false, CK.AppIdentity.ApplicationIdentityService? identity = null )
    {
        target ??= TestHelper.GetTypeScriptInlineTargetProjectPath( "StartAsync" );
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( CrisAspNetService ),
                                              typeof( CrisAspNetAuthenticationPackage ),
                                              typeof( CrisAuthenticationService ),
                                              typeof( AuthenticationInfo ),
                                              typeof( AuthenticationInfoDefaultProvider ),
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( IAuthenticationSessionOpener ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              // The endpoint side: the session command handlers.
                                              typeof( AuthenticationSessionEndpointService ),
                                              typeof( AuthenticationSessionHandlers ),
                                              typeof( BasicLoginHandler ),
                                              typeof( WeakLoginHandler ),
                                              typeof( WhoAmIHandler ),
                                              typeof( IWhoAmIQCommand ),
                                              typeof( IWhoCallsQCommand ),
                                              typeof( IUnsafeCommand ),
                                              typeof( UnsafeHandler ),
                                              typeof( IBasicLoginCommand ),
                                              typeof( IWeakLoginCommand ),
                                              typeof( ILogoutSessionCommand ),
                                              typeof( IRefreshSessionCommand ),
                                              typeof( IImpersonateCommand ),
                                              typeof( IClearImpersonationCommand ),
                                              typeof( IStartLoginCommand ),
                                              typeof( IRemoteLoginCallbackCommand ),
                                              typeof( IGetLoginSchemesQCommand ),
                                              typeof( ILoginSchemeInfo ),
                                              typeof( ISessionResult ),
                                              typeof( IAuthenticationInfoPoco ),
                                              typeof( IActorInfoPoco ),
                                              typeof( CK.Cris.AmbientValues.AmbientValuesService ),
                                              typeof( IAuthAmbientValues ),
                                              typeof( ICultureAmbientValues ) );
        // The authentication center side: its actors (FakeActorDatabase), its tokens and the operations.
        configuration.FirstBinPath.AddFakeAuthCenter();
        if( withOldStack ) configuration.FirstBinPath.Types.Add( typeof( CK.AspNet.Auth.AuthenticationInfoTokenService ), typeof( StdAuthenticationTypeSystem ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( target, typeof( IWhoAmIQCommand ), typeof( IWhoCallsQCommand ), typeof( IUnsafeCommand ), typeof( IBasicLoginCommand ), typeof( IWeakLoginCommand ), typeof( ILogoutSessionCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddCrisAuthentication();
        // The party model, when installed: the identity service and the CurrentLocalParty.
        if( identity != null )
        {
            builder.Services.AddSingleton<CK.AppIdentity.IApplicationIdentityService>( identity );
            builder.Services.AddScoped( _ => new CK.AppIdentity.CurrentLocalParty( identity ) );
        }
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

    // A refused call: its result is an ICrisResultError whose user messages are in the simplified format of the
    // TypeScript set, that the Json import cannot read back: the response is checked as text.
    static async Task<string> PostRefusedAsync( RunningAspNetServer server, string path, IPoco command )
    {
        var r = await server.Client.PostJsonAsync( path + "?UsePascalCase", ToJson( server, command ) );
        r.StatusCode.ShouldBe( HttpStatusCode.OK );
        var text = await r.Content.ReadAsStringAsync();
        text.ShouldContain( "CrisResultError" );
        return text;
    }

    [Test]
    public async Task login_sets_the_cookies_and_the_bearer_needs_the_binding_cookie_Async()
    {
        await using var server = await StartAsync();
        var client = server.Client;

        (await PostAsync( server, "/.cris", Create<IWhoAmIQCommand>( server ) )).Result.ShouldBe( "Anonymous" );

        // Login on "/.cris/auth".
        var r = await client.PostJsonAsync( "/.cris/auth?UsePascalCase", ToJson( server, Create<IBasicLoginCommand>( server, c =>
        {
            c.UserName = "Albert";
            c.Password = FakeActorDatabase.BasicPassword;
            c.RememberMe = true;
        } ) ) );
        var setCookies = r.Headers.GetValues( "Set-Cookie" ).ToList();
        var binding = setCookies.Single( c => c.StartsWith( CrisAuthenticationCookies.BindingCookieName + "=" ) ).ToLowerInvariant();
        binding.ShouldContain( "path=/.cris;" );
        binding.ShouldContain( "httponly" );
        binding.ShouldContain( "samesite=strict" );
        binding.ShouldContain( "expires=", customMessage: "RememberMe: persistent cookies." );
        setCookies.Single( c => c.StartsWith( CrisAuthenticationCookies.RefreshCookieName + "=" ) ).ToLowerInvariant().ShouldContain( "path=/.cris/auth" );
        var login = (ISessionResult)(await ReadResultAsync( server, r )).Result.ShouldNotBeNull();
        login.Info.UnsafeActor.ActorName.ShouldBe( "Albert" );

        // The bearer token with the binding cookie: authenticated.
        client.Token = login.Token;
        (await PostAsync( server, "/.cris", Create<IWhoAmIQCommand>( server ) )).Result.ShouldBe( "Albert" );

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
    public async Task ICommandAuthUnsafe_cannot_be_fooled_on_its_ActorId_Async()
    {
        await using var server = await StartAsync();
        var client = server.Client;
        UnsafeHandler.LastUserInfo = null;

        // Anonymous: the anonymous ActorId is accepted, a missing or another one is refused.
        (await PostAsync( server, "/.cris", Create<IUnsafeCommand>( server, c => { c.UserInfo = "Anonymous."; c.ActorId = 0; } ) )).Result.ShouldBeNull();
        UnsafeHandler.LastUserInfo.ShouldBe( "Anonymous." );
        (await PostRefusedAsync( server, "/.cris", Create<IUnsafeCommand>( server, c => c.UserInfo = "NO: no ActorId." ) ))
            .ShouldContain( "Invalid property: ActorId cannot be null." );
        (await PostRefusedAsync( server, "/.cris", Create<IUnsafeCommand>( server, c => { c.UserInfo = "NO WAY!"; c.ActorId = 42; } ) ))
            .ShouldContain( "Invalid actor identifier: the provided identifier" );

        // The Operator (42) is logged in.
        var r = await client.PostJsonAsync( "/.cris/auth?UsePascalCase", ToJson( server, Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) ) );
        client.Token = ((ISessionResult)(await ReadResultAsync( server, r )).Result.ShouldNotBeNull()).Token;
        (await PostAsync( server, "/.cris", Create<IUnsafeCommand>( server, c => { c.UserInfo = "Yes! The Operator is logged in."; c.ActorId = 42; } ) )).Result.ShouldBeNull();
        UnsafeHandler.LastUserInfo.ShouldBe( "Yes! The Operator is logged in." );
        (await PostRefusedAsync( server, "/.cris", Create<IUnsafeCommand>( server, c => { c.UserInfo = "NO WAY!"; c.ActorId = 7; } ) ))
            .ShouldContain( "Invalid actor identifier: the provided identifier" );

        // After the logout, the Operator is no more here.
        (await PostAsync( server, "/.cris/auth", Create<ILogoutSessionCommand>( server ) )).Result.ShouldBeNull();
        (await PostRefusedAsync( server, "/.cris", Create<IUnsafeCommand>( server, c => { c.UserInfo = "NO! The Operator is no more here."; c.ActorId = 42; } ) ))
            .ShouldContain( "Invalid actor identifier: the provided identifier" );
        UnsafeHandler.LastUserInfo.ShouldBe( "Yes! The Operator is logged in." );
    }

    [Test]
    public async Task session_commands_go_to_cris_auth_and_only_them_Async()
    {
        await using var server = await StartAsync();
        // The "/.cris" routes write the user messages in their simple format: the response text is checked.
        async Task<string> PostTextAsync( string path, IPoco command )
            => await (await server.Client.PostJsonAsync( path, ToJson( server, command ) )).Content.ReadAsStringAsync();
        var center = server.Services.GetRequiredService<FakeAuthCenterOperations>();
        // The route is declared by [CrisRoute<ICommandAuthenticationSession>( "/.cris/auth" )] on the CrisAspNetAuthenticationPackage.
        (await PostTextAsync( "/.cris", Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) )).ShouldContain( "must be sent to \\u0027/.cris/auth\\u0027" );
        // The legacy "/.cris/net" accepts only the commands of "/.cris".
        (await PostTextAsync( "/.cris/net", Create<IWeakLoginCommand>( server, c => c.WeakActorName = "Operator" ) )).ShouldContain( "must be sent to \\u0027/.cris/auth\\u0027" );
        center.LastOpen.ShouldBeNull( "The session command has not been executed." );
        (await PostTextAsync( "/.cris/auth", Create<IWhoAmIQCommand>( server ) )).ShouldContain( "must be sent to \\u0027/.cris\\u0027" );
        // An undeclared route accepts no command.
        (await PostTextAsync( "/.cris/other", Create<IWhoAmIQCommand>( server ) )).ShouldContain( "must be sent to \\u0027/.cris\\u0027" );
        (await PostTextAsync( "/.cris", Create<IWhoAmIQCommand>( server ) )).ShouldNotContain( "must be sent to" );
    }

    // Starts a remote login on "/.cris/auth": returns the provider's URL and the remote login cookie (its "name=value").
    static async Task<(RedirectedUrl Url, string Cookie, string SetCookie)> StartRemoteLoginAsync( RunningAspNetServer server, bool inline, string? returnUrl = null )
    {
        var r = await server.Client.PostJsonAsync( "/.cris/auth?UsePascalCase", ToJson( server, Create<IStartLoginCommand>( server, c =>
        {
            c.Scheme = "Oidc.Test";
            c.InlineMode = inline;
            c.ReturnUrl = returnUrl;
        } ) ) );
        var setCookie = r.Headers.GetValues( "Set-Cookie" ).Single( c => c.StartsWith( CrisAuthenticationCookies.RemoteLoginCookieName + "=" ) );
        var url = (RedirectedUrl)(await ReadResultAsync( server, r )).Result.ShouldNotBeNull();
        return (url, setCookie.Substring( 0, setCookie.IndexOf( ';' ) ), setCookie);
    }

    // The provider redirects the browser to the callback: a navigation, with the remote login cookie only (if any).
    static async Task<HttpResponseMessage> CallbackAsync( RunningAspNetServer server, string query, string? cookie )
    {
        using var http = new HttpClient( new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false } ) { BaseAddress = new Uri( server.ServerAddress ) };
        using var m = new HttpRequestMessage( HttpMethod.Get, CrisAuthenticationCookies.RemoteLoginCallbackPath + query );
        m.Headers.Add( "Sec-Fetch-Site", "cross-site" );
        if( cookie != null ) m.Headers.Add( "Cookie", cookie );
        return await http.SendAsync( m );
    }

    static string StateOf( RedirectedUrl url ) => url.Url.Substring( url.Url.IndexOf( "state=" ) + 6 );

    [Test]
    public async Task a_remote_login_in_a_popup_Async()
    {
        await using var server = await StartAsync();
        var (url, cookie, setCookie) = await StartRemoteLoginAsync( server, inline: false );
        url.IsInline.ShouldBeFalse();
        var center = server.Services.GetRequiredService<FakeAuthCenterOperations>();
        center.LastStartRemote!.CallbackUrl.ShouldBe( server.ServerAddress.TrimEnd( '/' ) + "/.auth/oidc/callback" );
        center.LastStartRemote.WebOrigin.ShouldBe( server.ServerAddress.TrimEnd( '/' ), "The origin that the session route requires from the browser." );
        var c = setCookie.ToLowerInvariant();
        c.ShouldContain( "path=/.auth/oidc/callback" );
        c.ShouldContain( "samesite=lax", customMessage: "The provider's redirection back is a cross-site navigation." );
        c.ShouldContain( "httponly" );
        c.ShouldContain( "expires=" );

        var r = await CallbackAsync( server, $"?state={StateOf( url )}&code=the-code", cookie );
        r.StatusCode.ShouldBe( HttpStatusCode.OK );
        r.Content.Headers.ContentType!.MediaType.ShouldBe( "text/html" );
        r.Headers.GetValues( "Content-Security-Policy" ).Single().ShouldStartWith( "default-src 'none'; script-src 'nonce-" );
        r.Headers.GetValues( "Referrer-Policy" ).Single().ShouldBe( "no-referrer" );
        r.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var html = await r.Content.ReadAsStringAsync();
        html.ShouldContain( """{"type":"ck-auth-remote-login","success":true,"error":null}""" );
        html.ShouldContain( "window.opener.postMessage(" );
        html.ShouldContain( "window.location.origin" );
        html.ShouldNotContain( "token", Case.Insensitive, "The page never carries a token." );
        var setCookies = r.Headers.GetValues( "Set-Cookie" ).ToList();
        setCookies.ShouldContain( s => s.StartsWith( CrisAuthenticationCookies.BindingCookieName + "=" ), "The session is issued: the client refreshes." );
        setCookies.ShouldContain( s => s.StartsWith( CrisAuthenticationCookies.RefreshCookieName + "=" ) );
        setCookies.Single( s => s.StartsWith( CrisAuthenticationCookies.RemoteLoginCookieName + "=" ) ).ShouldContain( "expires=Thu, 01 Jan 1970", customMessage: "Deleted: one use." );
    }

    [Test]
    public async Task a_remote_login_inline_Async()
    {
        await using var server = await StartAsync();
        var (url, cookie, _) = await StartRemoteLoginAsync( server, inline: true, returnUrl: "/after?x=1" );
        url.IsInline.ShouldBeTrue();
        var r = await CallbackAsync( server, $"?state={StateOf( url )}&code=the-code", cookie );
        r.StatusCode.ShouldBe( HttpStatusCode.SeeOther );
        r.Headers.Location!.ToString().ShouldBe( "/after?x=1&ck-auth-login=ok" );
        r.Headers.GetValues( "Set-Cookie" ).ShouldContain( s => s.StartsWith( CrisAuthenticationCookies.BindingCookieName + "=" ) );
    }

    [Test]
    public async Task a_remote_login_can_only_be_completed_by_the_browser_that_started_it_Async()
    {
        await using var server = await StartAsync();
        var (url, _, _) = await StartRemoteLoginAsync( server, inline: false );
        // No remote login cookie (another browser): refused.
        var r = await CallbackAsync( server, $"?state={StateOf( url )}&code=the-code", null );
        var html = await r.Content.ReadAsStringAsync();
        html.ShouldContain( """{"type":"ck-auth-remote-login","success":false,"error":"Test.Refused"}""" );
        r.Headers.TryGetValues( "Set-Cookie", out var setCookies );
        (setCookies ?? []).ShouldNotContain( s => s.StartsWith( CrisAuthenticationCookies.BindingCookieName + "=" ) && !s.Contains( "1970" ) );
    }

    [Test]
    public async Task the_return_url_of_an_inline_login_is_a_relative_path_Async()
    {
        await using var server = await StartAsync();
        // A forged remote login cookie with an absolute return URL: the redirection stays on this origin.
        var forged = CrisAuthenticationCookies.RemoteLoginCookieName + "=secret.i." + System.Buffers.Text.Base64Url.EncodeToString( Encoding.UTF8.GetBytes( "//evil.example.com/" ) );
        var r = await CallbackAsync( server, "?state=unknown&code=the-code", forged );
        r.StatusCode.ShouldBe( HttpStatusCode.SeeOther );
        r.Headers.Location!.ToString().ShouldBe( "/?ck-auth-login=Test.Refused" );
    }

    [Test]
    public async Task the_callback_command_cannot_be_sent_by_a_client_Async()
    {
        await using var server = await StartAsync();
        // It is not exchangeable with the clients (not in the "TypeScript" set): it cannot even be read. The middleware also
        // refuses it explicitly (a second barrier).
        var r = await server.Client.PostJsonAsync( "/.cris/auth", ToJson( server, Create<IRemoteLoginCallbackCommand>( server, c => { c.State = "s"; c.Code = FakeAuthCenterOperations.RemoteLoginCode; } ) ) );
        (await r.Content.ReadAsStringAsync()).ShouldContain( """{"result":["CrisResultError",{"isValidationError":true""" );
        server.Services.GetRequiredService<FakeAuthCenterOperations>().LastCompleteRemote.ShouldBeNull( "The handler has not been reached." );
        // The callback answers GET only.
        using var http = new HttpClient() { BaseAddress = new Uri( server.ServerAddress ) };
        (await http.PostAsync( CrisAuthenticationCookies.RemoteLoginCallbackPath, new StringContent( "" ) )).StatusCode.ShouldBe( HttpStatusCode.MethodNotAllowed );
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
        // The spec expects these schemes, and the Operator as the actor of a remote login.
        var center = server.Services.GetRequiredService<FakeAuthCenterOperations>();
        center.LoginSchemes.Clear();
        center.LoginSchemes.AddRange( [("Oidc.Test", TimeSpan.FromMinutes( 5 )), ("Weak", TimeSpan.Zero)] );
        center.RemoteLoginActorId = 42;
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

    [Test]
    public async Task a_request_received_directly_has_the_local_party_as_its_caller_party_Async()
    {
        // Without party model, the CallerPartyId is 0.
        await using( var server = await StartAsync() )
        {
            (await PostAsync( server, "/.cris", Create<IWhoCallsQCommand>( server ) )).Result.ShouldBe( 0 );
        }
        // With it, it is the PartyActorId of the current local party.
        var config = CK.AppIdentity.ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c => c["FullName"] = "Test/$App" ).ShouldNotBeNull();
        await using var identity = new CK.AppIdentity.ApplicationIdentityService( config, new ServiceCollection().BuildServiceProvider() );
        identity.TrySetPartyActorId( TestHelper.Monitor, 3712 ).ShouldBeTrue();
        await using( var server = await StartAsync( identity: identity ) )
        {
            (await PostAsync( server, "/.cris", Create<IWhoCallsQCommand>( server ) )).Result.ShouldBe( 3712 );
        }
    }
}
