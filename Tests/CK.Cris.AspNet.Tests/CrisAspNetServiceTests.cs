
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

// Object definition are in "Other" namespace: this tests that the generated code
// is "CK.Cris" namespace independent.
namespace Other
{
    using CK.Core;
    using CK.Cris;
    using System;

    /// <summary>
    /// Test command is in "Other" namespace.
    /// </summary>
    [ExternalName( "Test" )]
    public interface ITestCommand : ICommand
    {
        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        int Value { get; set; }
    }

    public class TestHandler : IAutoService
    {
        public static bool Called;

        [CommandHandler]
        public void Execute( ITestCommand cmd )
        {
            Called = true;
        }

        [CommandHandlingValidator]
        public void Validate( UserMessageCollector c, ITestCommand cmd )
        {
            if( cmd.Value <= 0 ) c.Error( "Value must be positive." );
        }
    }

    public class BuggyValidator : IAutoService
    {
        [CommandHandlingValidator]
        public void ValidateCommand( UserMessageCollector c, ITestCommand cmd )
        {
            throw new Exception( "This should not happen!" );
        }
    }


}

namespace CK.Cris.AspNet.Tests
{
    using CK.AspNet.Auth;
    using CK.Auth;
    using CK.Core;
    using CK.Testing;
    using Shouldly;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using Other;
    using System.Net.Http;
    using System.Threading.Tasks;
    using static CK.Testing.MonitorTestHelper;

    [TestFixture]
    public class CrisAspNetServiceTests
    {
        [Test]
        public async Task basic_call_to_a_command_handler_Async()
        {
            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Types.Add( typeof( ITestCommand ),
                                                  typeof( TestHandler ),
                                                  typeof( CrisExecutionContext ),
                                                  typeof( CrisAspNetService ),
                                                  typeof( AuthenticationInfoTokenService ),
                                                  typeof( StdAuthenticationTypeSystem ) );

            var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
            var builder = WebApplication.CreateSlimBuilder();
            builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
            await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map );
            var client = runningServer.Client;
            var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();

            // Value: 3712 is fine (it must be positive).
            {
                TestHandler.Called = false;
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""Test"",{""Value"":3712}]" );
                Throw.DebugAssert( r != null );
                TestHandler.Called.ShouldBeTrue();

                string typedResponse = await r.Content.ReadAsStringAsync();
                typedResponse.ShouldStartWith( @"{""result"":null," );

                var result = await pocoDirectory.GetCrisResultWithCorrelationIdSetToNullAsync( r );
                result.ToString().ShouldBe( @"{""Result"":null,""ValidationMessages"":null,""CorrelationId"":null}" );
            }
            // Value: 0 is invalid.
            {
                TestHandler.Called = false;
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""Test"",{""Value"":0}]" );
                Throw.DebugAssert( r != null );
                TestHandler.Called.ShouldBeFalse( "Validation error." );

                await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Value must be positive." ) );
            }
        }

        [Test]
        public async Task when_there_is_no_CommandHandler_it_is_directly_an_Execution_error_Async()
        {
            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Types.Add( typeof( ITestCommand ),
                                                  typeof( BuggyValidator ),
                                                  typeof( CrisAspNetService ),
                                                  typeof( AuthenticationInfoTokenService ),
                                                  typeof( StdAuthenticationTypeSystem ) );

            var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
            var builder = WebApplication.CreateSlimBuilder();
            builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
            await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map );
            var client = runningServer.Client;
            var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();

            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""Test"",{""Value"":3712}]" );
                Throw.DebugAssert( r != null );
                var result = await pocoDirectory.GetCrisResultAsync( r );
                result.ValidationMessages.ShouldBeNull( "Since there is no handler, there's no validation at all." );
                Throw.DebugAssert( result.Result != null );
                var resultError = (ICrisResultError)result.Result;
                resultError.IsValidationError.ShouldBeFalse();
            }
        }

        [Test]
        public async Task exceptions_raised_by_validators_are_handled_Async()
        {
            // To leak all exceptions in messages, CoreApplicationIdentity must be initialized and be in "#Dev" environment name.  
            CoreApplicationIdentity.Initialize();

            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Types.Add( typeof( ITestCommand ),
                                                  typeof( BuggyValidator ),
                                                  typeof( TestHandler ),
                                                  typeof( CrisAspNetService ),
                                                  typeof( AuthenticationInfoTokenService ),
                                                  typeof( StdAuthenticationTypeSystem ) );

            var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
            var builder = WebApplication.CreateSlimBuilder();
            builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
            await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map );
            var client = runningServer.Client;
            var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();

            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""Test"",{""Value"":3712}]" );
                Throw.DebugAssert( r != null );
                var result = await pocoDirectory.GetCrisResultAsync( r );
                result.CorrelationId.ShouldNotBeNullOrWhiteSpace();
                Throw.DebugAssert( result.ValidationMessages != null );
                result.ValidationMessages[0].Text.ShouldStartWith( "An unhandled error occurred while validating command 'Test' (LogKey: " );
                result.ValidationMessages[1].Text.ShouldBe( "This should not happen!" );
                // The ValidationMessages are the same as the ICrisResultError.
                Throw.DebugAssert( result.Result != null );
                var resultError = (ICrisResultError)result.Result;
                resultError.IsValidationError.ShouldBeTrue();
                resultError.Errors.ShouldBe( result.ValidationMessages );
            }
        }

        [Test]
        public async Task bad_request_are_validation_error_Async()
        {
            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Types.Add( typeof( CrisAspNetService ),
                                                  typeof( AuthenticationInfoTokenService ),
                                                  typeof( StdAuthenticationTypeSystem ) );

            var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
            var builder = WebApplication.CreateSlimBuilder();
            builder.AppendApplicationBuilder( app => app.UseMiddleware<CrisMiddleware>() );
            await using var runningServer = await builder.CreateRunningAspNetAuthenticationServerAsync( map );
            var client = runningServer.Client;
            var pocoDirectory = runningServer.Services.GetRequiredService<PocoDirectory>();
            // SimpleErrorResult.LogKey is null for really empty input.
            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, "" );
                Throw.DebugAssert( r != null );
                await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Unable to read Command Poco from empty request body." ) );
            }
            // Here SimpleErrorResult.LogKey is set.
            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, "----" );
                Throw.DebugAssert( r != null );
                await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Unable to read Command Poco from request body (byte length = 4)." ) );
            }
            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, "\"X\"" );
                Throw.DebugAssert( r != null );
                await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Unable to read Command Poco from request body (byte length = 3)." ) );
            }
            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, "{}" );
                Throw.DebugAssert( r != null );
                var result = await pocoDirectory.GetCrisResultWithCorrelationIdSetToNullAsync( r );
                await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Unable to read Command Poco from request body (byte length = 2)." ) );
            }
            {
                HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""Unknown"",{""value"":3712}]" );
                Throw.DebugAssert( r != null );
                var result = await pocoDirectory.GetCrisResultWithCorrelationIdSetToNullAsync( r );
                await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Unable to read Command Poco from request body (byte length = 26)." ) );
            }

        }

    }
}
