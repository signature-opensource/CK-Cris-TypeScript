#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using CK.Poco.Exc.Json;
using System.Buffers;
using System.Net;
using System.Text;
using CK.Core;
using CK.Setup;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.AspNet.Tests;

/// <summary>
/// The TypeFilterName of a "/.cris" request selects the type set of a TypeScript configuration: only its
/// commands can be received.
/// </summary>
[TestFixture]
public class TypeFilterTests
{
    public interface IPlainFilteredCommand : ICommand
    {
    }

    public interface IOtherFilteredCommand : ICommand
    {
    }

    public class Handlers : IAutoService
    {
        public static int Called;

        [CommandHandler]
        public void Handle( IPlainFilteredCommand cmd ) => Called++;

        [CommandHandler]
        public void Handle( IOtherFilteredCommand cmd ) => Called++;
    }

    // The targetProjectPath must be obtained by the test method (TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath).
    static async Task<RunningAspNetServer> CreateServerAsync( NormalizedPath targetProjectPath )
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IPlainFilteredCommand ),
                                              typeof( IOtherFilteredCommand ),
                                              typeof( Handlers ),
                                              typeof( CrisAspNetService ) );
        // The default "TypeScript" configuration exchanges the IPlainFilteredCommand.
        var ts = configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath, typeof( IPlainFilteredCommand ) );
        ts.IntegrationMode = CKGenIntegrationMode.None;
        // The "TypeScriptOther" configuration exchanges the IOtherFilteredCommand.
        var other = new TypeScriptBinPathAspectConfiguration
        {
            TargetProjectPath = targetProjectPath.RemoveLastPart().AppendPart( targetProjectPath.LastPart + "_Other" ),
            IntegrationMode = CKGenIntegrationMode.None,
            TypeFilterName = "TypeScriptOther"
        };
        other.Types.Add( typeof( IOtherFilteredCommand ), null );
        ts.AddOtherConfiguration( other );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
        return await builder.CreateRunningAspNetServerAsync( map );
    }

    static string ToJson( PocoDirectory pocoDirectory, IPoco command )
    {
        var w = new ArrayBufferWriter<byte>();
        pocoDirectory.WriteJson( w, command, withType: true, new PocoJsonExportOptions { TypeFilterName = "AllExchangeable", UseCamelCase = true } );
        return Encoding.UTF8.GetString( w.WrittenSpan );
    }

    [TestCase( "Nope" )]
    [TestCase( "TypeScriptNope" )]
    public async Task an_invalid_TypeFilterName_is_a_bad_request_Async( string typeFilterName )
    {
        await using var runningServer = await CreateServerAsync( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath() );
        var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();
        var plain = pocoDirectory.Create<IPlainFilteredCommand>();

        var response = await runningServer.Client.PostJsonAsync( $"/.cris?TypeFilterName={typeFilterName}", ToJson( pocoDirectory, plain ) );
        response.StatusCode.ShouldBe( HttpStatusCode.BadRequest );
        (await response.Content.ReadAsStringAsync()).ShouldContain( $"Invalid TypeFilterName \\u0027{typeFilterName}\\u0027." );
    }

    [Test]
    public async Task only_the_commands_of_the_TypeFilterName_can_be_received_Async()
    {
        await using var runningServer = await CreateServerAsync( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath() );
        var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();
        var plain = pocoDirectory.Create<IPlainFilteredCommand>();
        var other = pocoDirectory.Create<IOtherFilteredCommand>();

        async Task<string> PostAsync( string path, IPoco command )
        {
            var response = await runningServer.Client.PostJsonAsync( path, ToJson( pocoDirectory, command ) );
            response.StatusCode.ShouldBe( HttpStatusCode.OK );
            return await response.Content.ReadAsStringAsync();
        }

        Handlers.Called = 0;
        (await PostAsync( "/.cris", plain )).ShouldNotContain( "isValidationError\":true" );
        (await PostAsync( "/.cris?TypeFilterName=TypeScriptOther", other )).ShouldNotContain( "isValidationError\":true" );
        Handlers.Called.ShouldBe( 2 );

        // Each command is refused through the other TypeFilterName: it is not read.
        (await PostAsync( "/.cris", other )).ShouldContain( "Unable to read Command Poco" );
        (await PostAsync( "/.cris?TypeFilterName=TypeScriptOther", plain )).ShouldContain( "Unable to read Command Poco" );
        Handlers.Called.ShouldBe( 2 );
    }
}
