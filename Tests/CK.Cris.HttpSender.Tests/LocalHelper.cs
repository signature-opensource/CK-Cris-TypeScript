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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.HttpSender.Tests;

static class LocalHelper
{
    /// <summary>
    /// Starts a server: the HTTP front of the authentication on a fake authentication center, the <paramref name="commands"/>
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
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( IAuthenticationSessionOpener ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              // The endpoint side: the session command handlers.
                                              typeof( AuthenticationSessionEndpointService ),
                                              typeof( AuthenticationSessionHandlers ),
                                              typeof( BasicLoginHandler ),
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
        // The authentication center side (CK.Testing.Cris.Authentication): Albert (3712) logs in with "success".
        configuration.FirstBinPath.AddFakeAuthCenter();
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
