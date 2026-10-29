using CK.Auth;
using CK.Core;
using CK.Cris.AspNet;
using CK.Cris.AspNet.Authentication;
using CK.IO.Authentication;
using CK.IO.Authentication.Basic;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using AuthLevel = CK.Core.AuthLevel;

namespace CK.Cris.HttpSender.Tests;

/// <summary>
/// The actors of the fake server: Albert (3712) only.
/// </summary>
public sealed class FakeActors : IActorInfoProvider
{
    public const int AlbertId = 3712;
    public const string DeviceId = "the-albert-device-id";

    public static ActorInfo Albert( DateTime utcNow ) => new ActorInfo( AlbertId, "Albert", AuthLevel.Normal, [new SchemeInfo( "Basic", utcNow )] );

    public ValueTask<ActorInfo?> GetActorInfoAsync( IActivityMonitor monitor, int actorId )
        => ValueTask.FromResult<ActorInfo?>( actorId == AlbertId ? Albert( DateTime.UtcNow ) : null );
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
/// Opens a session for Albert.
/// </summary>
public sealed class FakeOpener : IAuthenticationSessionOpener
{
    readonly IAuthenticationSessionEndpoint _endpoint;

    public FakeOpener( IAuthenticationSessionEndpoint endpoint ) => _endpoint = endpoint;

    public ValueTask<ISessionResult> OpenSessionAsync<T>( IActivityMonitor monitor,
                                                          T command,
                                                          string scheme,
                                                          object payload,
                                                          bool rememberMe = false,
                                                          IReadOnlyDictionary<string, string?>? userData = null )
        where T : ICommand<ISessionResult>, ICommandAuthenticationSession
    {
        return ValueTask.FromResult( IssueAlbert( _endpoint, command, rememberMe ) );
    }

    internal static ISessionResult IssueAlbert( IAuthenticationSessionEndpoint endpoint, ICommand<ISessionResult> command, bool rememberMe )
    {
        var utcNow = DateTime.UtcNow;
        var proof = endpoint.Issue( $"{FakeActors.AlbertId}.the-session", rememberMe, utcNow.AddHours( 1 ) );
        var info = AuthenticationInfo.Create( FakeActors.Albert( utcNow ), utcNow.AddHours( 1 ), null, FakeActors.DeviceId, utcNow );
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

/// <summary>
/// The session commands: the basic login (Albert with "success"), the logout and the refresh.
/// </summary>
public sealed class FakeSessionHandlers : ISingletonAutoService
{
    [CommandHandler]
    public ValueTask<ISessionResult> HandleAsync( IActivityMonitor monitor, IBasicLoginCommand cmd, IAuthenticationSessionOpener opener )
    {
        if( cmd.UserName != "Albert" || cmd.Password != "success" )
        {
            return ValueTask.FromResult( cmd.CreateResult( r => r.Success = false ) );
        }
        return opener.OpenSessionAsync( monitor, cmd, "Basic", cmd, cmd.RememberMe );
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
        return FakeOpener.IssueAlbert( endpoint, cmd, rememberMe: true );
    }
}

static class LocalHelper
{
    /// <summary>
    /// Starts a fake server: the HTTP front of the authentication with the session commands, the <paramref name="commands"/>
    /// and the <paramref name="otherTypes"/> (their handlers).
    /// </summary>
    public static async Task<RunningAspNetServer> CreateRunningServerAsync( string testName, Type[] commands, params Type[] otherTypes )
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( CrisAspNetService ),
                                              typeof( CrisAspNetAuthenticationPackage ),
                                              typeof( CrisAuthenticationService ),
                                              typeof( AuthenticationInfo ),
                                              typeof( AuthenticationInfoDefaultProvider ),
                                              typeof( FakeActors ),
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( FakeTokenReader ),
                                              typeof( IAuthenticationSessionOpener ),
                                              typeof( FakeOpener ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              typeof( FakeSessionHandlers ),
                                              typeof( IBasicLoginCommand ),
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
        configuration.FirstBinPath.Types.Add( commands );
        configuration.FirstBinPath.Types.Add( otherTypes );
        // The HTTP front reads only the commands of the "TypeScript" type set.
        var tsTypes = commands.Concat( [typeof( IBasicLoginCommand ), typeof( ILogoutSessionCommand ), typeof( IRefreshSessionCommand )] ).ToArray();
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath( testName ), tsTypes );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddCrisAuthentication();
        return await builder.CreateRunningAspNetServerAsync( map, app =>
        {
            app.UseCrisAuthentication();
            app.UseCris();
        } );
    }

    public static async Task<IHost> CreateRunningCallerAsync( IStObjMap map, string serverAddress, CancellationToken cancellation = default )
    {
        var callerBuilder = Host.CreateEmptyApplicationBuilder( new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Development } );
        var c = callerBuilder.Configuration;
        c["CK-AppIdentity:FullName"] = "Domain/$Caller";
        c["CK-AppIdentity:Parties:0:FullName"] = "Domain/$Server";
        c["CK-AppIdentity:Parties:0:Address"] = serverAddress;
        if( Debugger.IsAttached )
        {
            // One hour timeout when Debugger.IsAttached.
            c["CK-AppIdentity:Parties:0:CrisHttpSender:Timeout"] = "00:01:00";
        }
        else
        {
            // Otherwise use the default 100 seconds timeout.
            c["CK-AppIdentity:Parties:0:CrisHttpSender"] = "true";
        }
        callerBuilder.AddApplicationIdentityServiceConfiguration();
        callerBuilder.Services.AddStObjMap( TestHelper.Monitor, map );
        var caller = callerBuilder.CKBuild();
        await caller.StartAsync( cancellation );
        return caller;
    }
}
