using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.HttpSender.Tests;

static class LocalHelper
{
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
