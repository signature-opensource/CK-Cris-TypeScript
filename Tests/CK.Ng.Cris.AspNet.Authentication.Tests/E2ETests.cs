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
using System.Collections.Generic;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Ng.Cris.AspNet.Authentication.Tests;

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
                                              typeof( IAuthenticationTokenReader ),
                                              typeof( IAuthenticationSessionEndpoint ),
                                              typeof( IBasicLoginCommand ),
                                              typeof( IWeakLoginCommand ),
                                              typeof( IStartPasskeyLoginCommand ),
                                              typeof( IPasskeyLoginCommand ),
                                              typeof( IStartPasskeyRegistrationCommand ),
                                              typeof( IPasskeyRegistrationCommand ),
                                              typeof( IPasskeyRegistrationResult ),
                                              typeof( ILogoutSessionCommand ),
                                              typeof( IRefreshSessionCommand ),
                                              typeof( ISessionResult ),
                                              typeof( IAuthenticationInfoPoco ),
                                              typeof( IActorInfoPoco ),
                                              typeof( CK.Cris.AmbientValues.AmbientValuesService ),
                                              typeof( IAuthAmbientValues ),
                                              typeof( ICultureAmbientValues ) );
        // The authentication center side (CK.Testing.Cris.Authentication): Albert (3712) logs in with "success", the weak
        // "Operator", the passkey ceremonies. The Ng packages bring the session command handlers.
        configuration.FirstBinPath.AddFakeAuthCenter();
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath, typeof( IBasicLoginCommand ), typeof( ILogoutSessionCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddCrisAuthentication();
        await using var runningServer = await builder.CreateRunningAspNetServerAsync( map, app =>
        {
            app.UseCrisAuthentication();
            app.UseCris();
        } );
        // "Oidc.Microsoft" cannot elevate an actor to the Critical level.
        var center = runningServer.Services.GetRequiredService<FakeAuthCenterOperations>();
        center.LoginSchemes.Clear();
        center.LoginSchemes.AddRange( [("Basic", TimeSpan.FromMinutes( 5 )),
                                       ("Oidc.Google", TimeSpan.FromMinutes( 5 )),
                                       ("Oidc.Microsoft", TimeSpan.Zero),
                                       ("Passkey", TimeSpan.FromMinutes( 5 ))] );
        await using var runner = TestHelper.CreateTypeScriptRunner( targetProjectPath, runningServer.ServerAddress );
        await TestHelper.SuspendAsync( resume => resume );
        runner.Run();
    }
}
