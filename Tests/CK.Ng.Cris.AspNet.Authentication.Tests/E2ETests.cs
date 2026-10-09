using CK.Auth;
using CK.Core;
using CK.Cris;
using CK.Cris.AspNet;
using CK.Cris.AspNet.Authentication;
using CK.IO.Authentication;
using CK.IO.Authentication.Basic;
using CK.IO.Authentication.Weak;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Ng.Cris.AspNet.Authentication.Tests;

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

/// <summary>
/// The fake login provider: "Albert" (actor 3712) with the password "success". The sessions are Normal.
/// </summary>
public sealed class FakeLoginHandlers : ISingletonAutoService
{
    static ISessionResult Issue( IAuthenticationSessionEndpoint endpoint, ICommand<ISessionResult> command, string name, bool rememberMe, bool weak = false )
    {
        var utcNow = DateTime.UtcNow;
        var proof = endpoint.Issue( weak ? "42.the-session" : "3712.the-session", rememberMe, utcNow.AddHours( 1 ) );
        var actor = weak
                        ? new ActorInfo( 42, name, AuthLevel.Unsafe, [new SchemeInfo( ActorInfo.WeakScheme, utcNow )] )
                        : new ActorInfo( 3712, name, AuthLevel.Normal, [new SchemeInfo( "Basic", utcNow )] );
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

    [CommandHandler]
    public ISessionResult Handle( IBasicLoginCommand cmd, IAuthenticationSessionEndpoint endpoint )
    {
        if( cmd.UserName != "Albert" || cmd.Password != "success" )
        {
            return cmd.CreateResult( r => { r.Success = false; r.UserMessages.Add( UserMessage.Error( NormalizedCultureInfo.CodeDefault, "Invalid credentials.", "Test.InvalidCredentials" ) ); } );
        }
        return Issue( endpoint, cmd, cmd.UserName, cmd.RememberMe );
    }

    [CommandHandler]
    public ISessionResult Handle( IWeakLoginCommand cmd, IAuthenticationSessionEndpoint endpoint )
    {
        if( cmd.WeakActorName != "Operator" )
        {
            return cmd.CreateResult( r => { r.Success = false; r.UserMessages.Add( UserMessage.Error( NormalizedCultureInfo.CodeDefault, "Unknown weak actor.", "Test.UnknownWeakActor" ) ); } );
        }
        return Issue( endpoint, cmd, cmd.WeakActorName, rememberMe: false, weak: true );
    }

    [CommandHandler]
    public ISessionResult Handle( IRefreshSessionCommand cmd, IAuthenticationSessionEndpoint endpoint )
    {
        if( endpoint.PresentedProof == null )
        {
            endpoint.Clear();
            return cmd.CreateResult( r => r.Success = false );
        }
        return Issue( endpoint, cmd, "Albert", rememberMe: true );
    }

    [CommandHandler]
    public void Handle( ILogoutSessionCommand cmd, IAuthenticationSessionEndpoint endpoint ) => endpoint.Clear();

    [CommandHandler]
    public IList<string> Handle( IGetLoginSchemesQCommand cmd ) => ["Basic", "Oidc.Google", "Oidc.Microsoft"];
}

public class E2ETests
{
    [Test]
    public async Task CK_Ng_Cris_AspNet_Authentication_Tests_Async()
    {
        var targetProjectPath = TestHelper.GetTypeScriptInlineTargetProjectPath();

        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Path = TestHelper.BinFolder;
        configuration.FirstBinPath.Assemblies.Add( "CK.Ng.Cris.AspNet.Authentication.Basic" );
        configuration.FirstBinPath.Assemblies.Add( "CK.Ng.Cris.AspNet.Authentication.Weak" );
        configuration.FirstBinPath.Types.Add( typeof( CrisAspNetService ),
                                              typeof( CrisAuthenticationService ),
                                              typeof( AuthenticationInfo ),
                                              typeof( AuthenticationInfoDefaultProvider ),
                                              typeof( NoActor ),
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( FakeTokenReader ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              typeof( FakeLoginHandlers ),
                                              typeof( IBasicLoginCommand ),
                                              typeof( IWeakLoginCommand ),
                                              typeof( ILogoutSessionCommand ),
                                              typeof( IRefreshSessionCommand ),
                                              typeof( ISessionResult ),
                                              typeof( IAuthenticationInfoPoco ),
                                              typeof( IActorInfoPoco ),
                                              typeof( CK.Cris.AmbientValues.AmbientValuesService ),
                                              typeof( IAuthAmbientValues ),
                                              typeof( ICultureAmbientValues ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath, typeof( IBasicLoginCommand ), typeof( ILogoutSessionCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddCrisAuthentication();
        await using var runningServer = await builder.CreateRunningAspNetServerAsync( map, app =>
        {
            app.UseCrisAuthentication();
            app.UseCris();
        } );
        await using var runner = TestHelper.CreateTypeScriptRunner( targetProjectPath, runningServer.ServerAddress );
        await TestHelper.SuspendAsync( resume => resume );
        runner.Run();
    }
}
