#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using CK.Core;
using CK.Poco.Exc.Json;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.AspNet.Tests;

/// <summary>
/// HTTP receives only the public commands (on every route), and a command whose incoming validation configures ambient
/// services executes inline (it never waits for the background executor).
/// </summary>
[TestFixture]
public class ExposureAndInlineTests
{
    public interface IPublicCommand : ICommand<string>
    {
    }

    [Internal]
    public interface IInternalCommand : ICommand<string>
    {
    }

    [Private]
    public interface IPrivateCommand : ICommand<string>
    {
    }

    // Its culture is configured by its incoming validation (CrisCultureService).
    public interface ICultureCommand : ICommand<string>, ICurrentCulturePart
    {
    }

    // Occupies the background executor's runner until it is released.
    public interface IBlockingCommand : ICommand
    {
    }

    public sealed class Handlers : ISingletonAutoService
    {
        public static TaskCompletionSource Release = new( TaskCreationOptions.RunContinuationsAsynchronously );

        [CommandHandler]
        public string Handle( IPublicCommand c ) => "public";

        [CommandHandler]
        public string Handle( IInternalCommand c ) => "internal";

        [CommandHandler]
        public string Handle( IPrivateCommand c ) => "private";

        [CommandHandler]
        public string Handle( ICultureCommand c, ExtendedCultureInfo culture ) => culture.Name;

        [CommandHandler]
        public Task HandleAsync( IBlockingCommand c ) => Release.Task;
    }

    [Test]
    public async Task only_the_public_commands_are_received_and_a_configured_command_executes_inline_Async()
    {
        NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr" );
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IPublicCommand ),
                                              typeof( IInternalCommand ),
                                              typeof( IPrivateCommand ),
                                              typeof( ICultureCommand ),
                                              typeof( IBlockingCommand ),
                                              typeof( Handlers ),
                                              typeof( CrisCultureService ),
                                              typeof( CrisAspNetService ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( TestHelper.GetTypeScriptInlineTargetProjectPath(),
                                                                        typeof( IPublicCommand ),
                                                                        typeof( ICultureCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
        await using var runningServer = await builder.CreateRunningAspNetServerAsync( map );
        var pocos = runningServer.Services.GetRequiredService<PocoDirectory>();

        async Task<string> PostAsync( string path, IPoco command, PocoJsonExportOptions options )
        {
            var w = new ArrayBufferWriter<byte>();
            pocos.WriteJson( w, command, withType: true, options );
            return await (await runningServer.Client.PostJsonAsync( path, Encoding.UTF8.GetString( w.WrittenSpan ) )).Content.ReadAsStringAsync();
        }
        var ts = new PocoJsonExportOptions { TypeFilterName = "TypeScript", UseCamelCase = true };
        var net = PocoJsonExportOptions.Default;

        // "/.cris/net" reads all the exchangeable types: only the public commands are executed.
        (await PostAsync( "/.cris/net", pocos.Create<IPublicCommand>(), net )).ShouldContain( "public" );
        (await PostAsync( "/.cris/net", pocos.Create<IInternalCommand>(), net )).ShouldContain( "is not accepted from HTTP" );
        (await PostAsync( "/.cris/net", pocos.Create<IPrivateCommand>(), net )).ShouldContain( "is not accepted from HTTP" );

        // The background executor's single runner is busy: a command with a configured culture doesn't wait for it.
        Handlers.Release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        var background = runningServer.Services.GetRequiredService<CrisBackgroundExecutorService>();
        var blocking = background.Submit( TestHelper.Monitor, pocos.Create<IBlockingCommand>(), null );
        try
        {
            var answer = await PostAsync( "/.cris", pocos.Create<ICultureCommand>( c => c.CurrentCultureName = "fr" ), ts ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
            answer.ShouldContain( "\"fr\"" );
            blocking.ExecutedCommand.IsCompleted.ShouldBeFalse( "The runner is still busy." );
        }
        finally
        {
            Handlers.Release.TrySetResult();
        }
        await blocking.ExecutedCommand.WaitAsync( TimeSpan.FromSeconds( 10 ) );
    }

    [Test]
    public async Task an_Internal_or_Private_command_cannot_be_a_TypeScript_type_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IPublicCommand ),
                                              typeof( IInternalCommand ),
                                              typeof( Handlers ),
                                              typeof( CrisAspNetService ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( TestHelper.GetTypeScriptInlineTargetProjectPath(),
                                                                        typeof( IPublicCommand ),
                                                                        typeof( IInternalCommand ) );
        await configuration.GetFailedAutomaticServicesAsync( "Command 'CK.Cris.AspNet.Tests.ExposureAndInlineTests.IInternalCommand' is [Internal]: it cannot be a TypeScript type" );
    }
}
