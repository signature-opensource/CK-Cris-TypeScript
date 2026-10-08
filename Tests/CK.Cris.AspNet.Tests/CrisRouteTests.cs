using CK.AspNet.Auth;
using CK.Auth;
using CK.Poco.Exc.Json;
using System.Buffers;
using System.Text;
using CK.Core;
using CK.Testing;
using CK.TypeScript;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.AspNet.Tests;

[TestFixture]
public class CrisRouteTests
{
    public interface ICommandAdminPart : ICommandPart
    {
    }

    public interface ICommandOtherPart : ICommandPart
    {
    }

    public interface IAdminCommand : ICommand, ICommandAdminPart
    {
        int Value { get; set; }
    }

    public interface IBothCommand : ICommand, ICommandAdminPart, ICommandOtherPart
    {
    }

    public interface IPlainCommand : ICommand
    {
    }

    public class Handlers : IAutoService
    {
        public static int Called;

        [CommandHandler]
        public void Handle( IAdminCommand cmd ) => Called++;

        [CommandHandler]
        public void Handle( IPlainCommand cmd ) => Called++;

        [CommandHandler]
        public void Handle( IBothCommand cmd ) => Called++;
    }

    [TypeScriptPackage]
    [CrisRoute<ICommandAdminPart>( "/.cris/admin" )]
    public class AdminPackage : TypeScriptPackage
    {
    }

    [TypeScriptPackage]
    [CrisRoute<ICommandOtherPart>( "/.cris/other" )]
    public class OtherPackage : TypeScriptPackage
    {
    }

    [TypeScriptPackage]
    [CrisRoute<ICommandAdminPart>( "/.cris/not-admin" )]
    public class ConflictingPackage : TypeScriptPackage
    {
    }

    [TypeScriptPackage]
    [CrisRoute<ICommandAdminPart>( "/api/admin" )]
    public class OutsideCrisPackage : TypeScriptPackage
    {
    }

    [TypeScriptPackage]
    [CrisRoute<ICommandAdminPart>( "/.cris/net" )]
    public class ReservedPackage : TypeScriptPackage
    {
    }

    [CrisRoute<ICommandAdminPart>( "/.cris/admin" )]
    public class NotAPackage : IAutoService
    {
    }

    [Test]
    public async Task a_command_is_accepted_only_on_its_route_Async()
    {
        var target = TestHelper.GetTypeScriptInlineTargetProjectPath();
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IAdminCommand ),
                                              typeof( IPlainCommand ),
                                              typeof( Handlers ),
                                              typeof( AdminPackage ),
                                              typeof( CrisAspNetService ),
                                              typeof( AuthenticationInfoTokenService ),
                                              typeof( StdAuthenticationTypeSystem ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( target, typeof( IAdminCommand ), typeof( IPlainCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
        await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map );
        var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();

        string ToJson( IPoco command )
        {
            var w = new ArrayBufferWriter<byte>();
            pocoDirectory.WriteJson( w, command, withType: true, new PocoJsonExportOptions { TypeFilterName = "TypeScript", UseCamelCase = true } );
            return Encoding.UTF8.GetString( w.WrittenSpan );
        }
        async Task<string> PostAsync( string path, IPoco command )
            => await (await runningServer.Client.PostJsonAsync( path, ToJson( command ) )).Content.ReadAsStringAsync();

        var admin = pocoDirectory.Create<IAdminCommand>( c => c.Value = 1 );
        var plain = pocoDirectory.Create<IPlainCommand>();
        Handlers.Called = 0;
        (await PostAsync( "/.cris/admin", admin )).ShouldNotContain( "must be sent to" );
        (await PostAsync( "/.cris", plain )).ShouldNotContain( "must be sent to" );
        Handlers.Called.ShouldBe( 2 );

        (await PostAsync( "/.cris", admin )).ShouldContain( "must be sent to \\u0027/.cris/admin\\u0027" );
        (await PostAsync( "/.cris/admin", plain )).ShouldContain( "must be sent to \\u0027/.cris\\u0027" );
        (await PostAsync( "/.cris/admin/sub", admin )).ShouldContain( "must be sent to \\u0027/.cris/admin\\u0027" );
        Handlers.Called.ShouldBe( 2 );
    }

    [Test]
    public async Task a_command_cannot_have_two_routes_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IBothCommand ),
                                              typeof( Handlers ),
                                              typeof( AdminPackage ),
                                              typeof( OtherPackage ),
                                              typeof( CrisAspNetService ) );
        await configuration.GetFailedAutomaticServicesAsync( "Command 'CK.Cris.AspNet.Tests.CrisRouteTests.IBothCommand' has more than one route" );
    }

    [Test]
    public async Task a_command_part_cannot_have_two_routes_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IAdminCommand ),
                                              typeof( Handlers ),
                                              typeof( AdminPackage ),
                                              typeof( ConflictingPackage ),
                                              typeof( CrisAspNetService ) );
        await configuration.GetFailedAutomaticServicesAsync( "Command part 'CK.Cris.AspNet.Tests.CrisRouteTests.ICommandAdminPart' has two routes" );
    }

    [TestCase( typeof( OutsideCrisPackage ), "invalid route '/api/admin'" )]
    [TestCase( typeof( ReservedPackage ), "invalid route '/.cris/net'" )]
    [TestCase( typeof( NotAPackage ), "a route must be declared on the IResourcePackage" )]
    public async Task invalid_route_declarations_Async( System.Type declarer, string message )
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IAdminCommand ),
                                              typeof( Handlers ),
                                              declarer,
                                              typeof( CrisAspNetService ) );
        await configuration.GetFailedAutomaticServicesAsync( message );
    }
}
