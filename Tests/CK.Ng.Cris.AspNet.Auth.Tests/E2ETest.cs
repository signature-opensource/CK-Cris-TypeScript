using CK.Auth;
using CK.Cris.AspNet;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;
using System.Threading.Tasks;
using CK.Testing;
using Microsoft.Extensions.Hosting;
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
        var tsConfig = configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath, typeof( IBasicLoginCommand ),
                                                                                                          typeof( ILogoutCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();

        var builder = WebApplication.CreateSlimBuilder();
        builder.AddApplicationIdentityServiceConfiguration();
        await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map, configureApplication: app => app.UseMiddleware<CrisMiddleware>() );
        await using var runner = TestHelper.CreateTypeScriptRunner( targetProjectPath );
        await TestHelper.SuspendAsync( resume => resume );
        runner.Run();
    }
}
