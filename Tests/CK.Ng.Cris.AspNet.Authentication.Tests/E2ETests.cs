using CK.Auth;
using CK.Core;
using CK.Cris;
using CK.Cris.AspNet;
using CK.Cris.AspNet.Authentication;
using CK.IO.Authentication;
using CK.IO.Authentication.Basic;
using CK.IO.Authentication.Passkey;
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
/// The fake authentication center: the operations that the session command handlers of the endpoint
/// (CK.Cris.Authentication and its .Basic, .Weak and .Passkey packages, brought by the Ng packages) execute.
/// The Basic provider knows "Albert" (actor 3712) with the password "success"; the weak provider knows "Operator".
/// </summary>
public sealed class FakeAuthCenterOperations : ISingletonAutoService
{
    // A login gives a Normal session (Critical for a passkey login, like the real authentication center).
    static ISessionOperationResult Issue<T>( T op, PocoDirectory pocos, string name, bool rememberMe, bool weak = false, bool critical = false, TimeSpan? criticalLifetime = null )
        where T : ICommand<ISessionOperationResult>, ICommandNextSessionSecrets
    {
        var utcNow = DateTime.UtcNow;
        var actor = weak
                        ? new ActorInfo( 42, name, CK.Core.AuthLevel.Unsafe, [new SchemeInfo( ActorInfo.WeakScheme, utcNow )] )
                        : new ActorInfo( 3712, name, critical ? CK.Core.AuthLevel.Critical : CK.Core.AuthLevel.Normal, [new SchemeInfo( critical ? "Passkey" : "Basic", utcNow )] );
        var info = AuthenticationInfo.Create( actor, utcNow.AddHours( 1 ), critical ? utcNow + (criticalLifetime ?? TimeSpan.FromMinutes( 5 )) : null, "D", utcNow );
        var token = "token-" + Guid.NewGuid().ToString( "N" );
        FakeTokenReader.Tokens[token] = new AuthenticationTokenData( info, op.NextBindingSecretHash );
        return op.CreateResult( r =>
        {
            r.IssuedSessionKey = weak ? "42.the-session" : "3712.the-session";
            r.RememberMe = rememberMe;
            r.RefreshExpires = utcNow.AddHours( 1 );
            r.Result = pocos.Create<ISessionResult>( s =>
            {
                s.Info.InitializeFrom( info );
                s.Token = token;
                s.RememberMe = rememberMe;
            } );
        } );
    }

    static ISessionOperationResult Refuse( ICommand<ISessionOperationResult> op, PocoDirectory pocos, string text, string resName )
        => op.CreateResult( r => r.Result = pocos.Create<ISessionResult>( s =>
        {
            s.Success = false;
            s.UserMessages.Add( UserMessage.Error( NormalizedCultureInfo.CodeDefault, text, resName ) );
        } ) );

    [CommandHandler]
    public ISessionOperationResult Handle( IOpenSessionOperationCommand op, PocoDirectory pocos )
    {
        switch( op.Payload )
        {
            case IBasicLoginCommand basic:
                // "critical-1s": a Critical session that ends one second later.
                if( basic.UserName == "Albert" && basic.Password == "critical-1s" )
                {
                    return Issue( op, pocos, basic.UserName, op.RememberMe, critical: true, criticalLifetime: TimeSpan.FromSeconds( 1 ) );
                }
                if( basic.UserName != "Albert" || basic.Password != "success" )
                {
                    return Refuse( op, pocos, "Invalid credentials.", "Test.InvalidCredentials" );
                }
                return Issue( op, pocos, basic.UserName, op.RememberMe );
            case IWeakLoginCommand weak:
                if( weak.WeakActorName != "Operator" )
                {
                    return Refuse( op, pocos, "Unknown weak actor.", "Test.UnknownWeakActor" );
                }
                return Issue( op, pocos, weak.WeakActorName, rememberMe: false, weak: true );
            default:
                return Refuse( op, pocos, "Unknown scheme.", "Test.UnknownScheme" );
        }
    }

    [CommandHandler]
    public ISessionOperationResult Handle( IRefreshSessionOperationCommand op, PocoDirectory pocos )
    {
        if( op.PresentedSessionKey == null )
        {
            return op.CreateResult( r =>
            {
                r.Cleared = true;
                r.Result = pocos.Create<ISessionResult>( s => s.Success = false );
            } );
        }
        return Issue( op, pocos, "Albert", rememberMe: true );
    }

    [CommandHandler]
    public ISessionOperationResult Handle( ILogoutSessionOperationCommand op ) => op.CreateResult( r => r.Cleared = true );

    // "Oidc.Microsoft" cannot elevate an actor to the Critical level.
    [CommandHandler]
    public IList<ILoginSchemeInfo> Handle( IGetLoginSchemesQCommand cmd, PocoDirectory pocos )
        => [Scheme( pocos, "Basic", 5 ), Scheme( pocos, "Oidc.Google", 5 ), Scheme( pocos, "Oidc.Microsoft", 0 ), Scheme( pocos, "Passkey", 5 )];

    static ILoginSchemeInfo Scheme( PocoDirectory pocos, string name, int criticalMinutes )
        => pocos.Create<ILoginSchemeInfo>( s => { s.Name = name; s.CriticalLifetime = TimeSpan.FromMinutes( criticalMinutes ); } );

    // The passkey ceremonies: the browser's part is mocked by the spec, these fakes check what it sends.
    [CommandHandler]
    public string Handle( IStartPasskeyLoginOperationCommand op ) => """{"challenge":"login-challenge","rpId":"localhost","allowCredentials":[],"userVerification":"required"}""";

    [CommandHandler]
    public ISessionOperationResult Handle( IFinishPasskeyLoginOperationCommand op, PocoDirectory pocos )
    {
        var login = op.Login!;
        if( !login.Response.Contains( "\"signed\":\"login-challenge\"" ) )
        {
            return Refuse( op, pocos, "The passkey cannot be verified.", "Authentication.PasskeyVerificationFailed" );
        }
        return Issue( op, pocos, "Albert", login.RememberMe, critical: true );
    }

    [CommandHandler]
    public string Handle( IStartPasskeyRegistrationOperationCommand op ) => """{"challenge":"registration-challenge","rp":{"id":"localhost","name":"Test"},"user":{"id":"dXNlcg","name":"Albert","displayName":"Albert"},"pubKeyCredParams":[{"type":"public-key","alg":-7}]}""";

    [CommandHandler]
    public IPasskeyRegistrationResult Handle( IFinishPasskeyRegistrationOperationCommand op ) => op.CreateResult( r => r.Name = "Test passkey" );
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
        configuration.FirstBinPath.Assemblies.Add( "CK.Ng.Cris.AspNet.Authentication.Passkey" );
        configuration.FirstBinPath.Types.Add( typeof( CrisAspNetService ),
                                              typeof( CrisAuthenticationService ),
                                              typeof( AuthenticationInfo ),
                                              typeof( AuthenticationInfoDefaultProvider ),
                                              typeof( NoActor ),
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( FakeTokenReader ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              typeof( FakeAuthCenterOperations ),
                                              typeof( IBasicLoginCommand ),
                                              typeof( IWeakLoginCommand ),
                                              typeof( IStartPasskeyLoginCommand ),
                                              typeof( IPasskeyLoginCommand ),
                                              typeof( IStartPasskeyRegistrationCommand ),
                                              typeof( IPasskeyRegistrationCommand ),
                                              typeof( IPasskeyRegistrationResult ),
                                              typeof( ILogoutSessionCommand ),
                                              typeof( IRefreshSessionCommand ),
                                              typeof( IOpenSessionOperationCommand ),
                                              typeof( IStartPasskeyLoginOperationCommand ),
                                              typeof( IFinishPasskeyLoginOperationCommand ),
                                              typeof( IStartPasskeyRegistrationOperationCommand ),
                                              typeof( IFinishPasskeyRegistrationOperationCommand ),
                                              typeof( IRefreshSessionOperationCommand ),
                                              typeof( ILogoutSessionOperationCommand ),
                                              typeof( ISessionOperationResult ),
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
