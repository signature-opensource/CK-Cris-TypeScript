using CK.Auth;
using CK.Cris;
using CK.Cris.AspNet;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Ng.Cris.AspNet.Auth.Tests;

public class E2ETest
{
    [Test]
    public async Task CK_Ng_Cris_AspNet_Auth_Tests_Async()
    {
        var targetProjectPath = TestHelper.GetTypeScriptInlineTargetProjectPath();

        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Path = TestHelper.BinFolder;
        configuration.FirstBinPath.Assemblies.Add( "CK.Cris.Auth" );
        configuration.FirstBinPath.Assemblies.Add( "CK.IO.Auth.Basic" );
        configuration.FirstBinPath.Assemblies.Add( "CK.Cris.AspNet" );
        configuration.FirstBinPath.Assemblies.Add( "CK.Ng.Cris.AspNet.Auth" );
        configuration.FirstBinPath.Assemblies.Add( "CK.Cris.AspNet.Auth.Basic" );

        configuration.FirstBinPath.Types.Add( typeof( ITestBatchCommand ),
                                              typeof( ITestCommand ),
                                              typeof( IBasicTestCommand ) );

        var tsConfig = configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath, typeof( IBasicLoginCommand ),
                                                                                                          typeof( ILogoutCommand ),
                                                                                                          typeof( ITestBatchCommand ),
                                                                                                          typeof( ITestCommand ),
                                                                                                          typeof( ICrisBasicCommandResult ),
                                                                                                          typeof( IBasicTestCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();

        var builder = WebApplication.CreateSlimBuilder();
        builder.AddApplicationIdentityServiceConfiguration();
        await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map, configureApplication: app => app.UseMiddleware<CrisMiddleware>() );
        await using var runner = TestHelper.CreateTypeScriptRunner( targetProjectPath, runningServer.ServerAddress );
        await TestHelper.SuspendAsync( resume => resume );
        runner.Run();
    }
}
