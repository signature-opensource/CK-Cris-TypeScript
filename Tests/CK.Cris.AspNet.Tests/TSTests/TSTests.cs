using CK.AspNet.Auth;
using CK.Auth;
using CK.Core;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace CK.Cris.AspNet.E2ETests;

[TestFixture]
public class TSTests
{
    /// <summary>
    /// Secondary Poco that defines the "Color" as a Endpoint value.
    /// </summary>
    public interface IColoredEndpointValues : AmbientValues.IAmbientValues
    {
        /// <summary>
        /// The color of <see cref="ICommandColored"/> commands.
        /// </summary>
        string Color { get; set; }
    }

    /// <summary>
    /// This command is empty but returns a SimpleUserMessage.
    /// </summary>
    public interface IWithMessageCommand : ICommand<SimpleUserMessage>
    {
    }

    /// <summary>
    /// This command is empty but returns a UserMessage but CK.TypeScript
    /// maps UserMessage to the SimpleUserMessage TS type.
    /// </summary>
    public interface IWithUserMessageCommand : ICommand<UserMessage>
    {
    }

    public class ColorAndBuggyService : IAutoService
    {
        /// <summary>
        /// Ambient value collector for <see cref="IColoredEndpointValues.Color"/>.
        /// </summary>
        /// <param name="cmd">The collec command is here only to trigger the collect.</param>
        /// <param name="values">The command result to update.</param>
        [CommandPostHandler]
        public void GetColoredAmbientValues( AmbientValues.IAmbientValuesCollectCommand cmd, IColoredEndpointValues values )
        {
            values.Color = "Red";
        }

        [CommandHandler]
        public string HandleBeautifulCommand( IBeautifulCommand cmd )
        {
            return $"{cmd.Color} - {cmd.Beauty}";
        }

        [CommandHandler]
        public SimpleUserMessage Handle( CurrentCultureInfo culture, IWithMessageCommand cmd )
        {
            return culture.InfoMessage( $"Local server time is {DateTime.Now}." );
        }

        [CommandHandler]
        public UserMessage Handle( CurrentCultureInfo culture, IWithUserMessageCommand cmd )
        {
            return culture.WarnMessage( $"I'm a UserMessage and it is {DateTime.Now}." ).With( 12 );
        }

        [CommandHandlingValidator]
        public void ValidateBuggyCommand( UserMessageCollector collector, IBuggyCommand cmd )
        {
            using( collector.OpenInfo( "This is an info from the command validation." ) )
            {
                if( cmd.EmitValidationError )
                {
                    collector.Error( "The BuggyCommand is not valid (by design)." );
                }
                collector.Warn( "This is a warning from the command validation." );
            }
        }

        [CommandHandler]
        public void HandleBuggyCommand( IBuggyCommand cmd )
        {
            Throw.DebugAssert( cmd.EmitValidationError is false );
            throw new System.NotImplementedException( "BuggyCommand handler is not implemented." );
        }


    }

    /// <summary>
    /// Command part with the color endpoint property.
    /// </summary>
    public interface ICommandColored : ICommandPart
    {
        /// <summary>
        /// Gets or sets the color.
        /// <para>
        /// This is an ubiquitous value: the caller can set it but if not, the TypeScript
        /// CrisEndPoint transparently sets it.
        /// </para>
        /// </summary>
        [AmbientServiceValue]
        string? Color { get; set; }
    }

    /// <summary>
    /// A beautiful command has a <see cref="Beauty"/> and is a <see cref="ICommandColored"/>:
    /// the color is managed automatically.
    /// </summary>
    public interface IBeautifulCommand : ICommandColored, ICommand<string>
    {
        /// <summary>
        /// Gets or sets the beauty's string.
        /// </summary>
        string Beauty { get; set; }
    }

    public interface IBuggyCommand : ICommand
    {
        /// <summary>
        /// Gets or sets whether a validation error must be emitted or
        /// a <see cref="System.NotImplementedException"/> must be thrown
        /// from command execution.
        /// </summary>
        bool EmitValidationError { get; set; }
    }

    [Test]
    public async Task E2ETestWithCommands_Async()
    {
        var targetProjectPath = TestHelper.GetTypeScriptInlineTargetProjectPath();

        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( ICommand<> ), // Useless but harmless.
                                              typeof( IBeautifulCommand ),
                                              typeof( ICommandColored ),
                                              typeof( ICultureAmbientValues ),
                                              typeof( AmbientValues.AmbientValuesService ),
                                              typeof( IColoredEndpointValues ),
                                              typeof( ColorAndBuggyService ),
                                              typeof( IBuggyCommand ),
                                              typeof( IWithMessageCommand ),
                                              typeof( IWithUserMessageCommand ),
                                              typeof( CrisAspNetService ),
                                              typeof( AuthenticationInfoTokenService ),
                                              typeof( StdAuthenticationTypeSystem ) );
        configuration.FirstBinPath.EnsureTypeScriptConfigurationAspect( targetProjectPath, typeof( ICommand<> ), // Useless but harmless.
                                                                                           typeof( IBeautifulCommand ),
                                                                                           typeof( IBuggyCommand ),
                                                                                           typeof( IWithMessageCommand ),
                                                                                           typeof( IWithUserMessageCommand ) );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        var builder = WebApplication.CreateSlimBuilder();
        await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map, configureApplication: app => app.UseMiddleware<CrisMiddleware>() );

        await using var runner = TestHelper.CreateTypeScriptRunner( targetProjectPath, runningServer.ServerAddress, new Dictionary<string, string> { { "CRIS_ENDPOINT_URL", runningServer.ServerAddress + "/.cris" } } );
        // When running in Debug, this will wait until resume is set to true.
        // Until then, the .NET server is running and tests can be manually executed
        // written and fixed.
        // 
        // To stop, simply put a breakpoint in the resume lambda and sets the resume value
        // to true with the Watch window.
        //
        // In regular run, this will not wait for resume.
        //
        await TestHelper.SuspendAsync( resume => resume );

        // Executes the TypeScript tests.
        runner.Run();
    }
}
