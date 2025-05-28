using CK.AspNet.Auth;
using CK.Auth;
using CK.Core;
using CK.Testing;
using Shouldly;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace CK.Cris.AspNet.Tests;

[TestFixture]
public class CommandWithCurrentCultureTests
{
    /// <summary>
    /// Secondary <see cref="ICommandCurrentCulture"/> that adds a IsValid property.
    /// </summary>
    [ExternalName( "TestCommand" )]
    public interface ITestCommand : ICommand<string>, ICommandCurrentCulture
    {
        /// <summary>
        /// Gets or sets whether this is a valid incoming command.
        /// When false, the command will not be validated by the [IncomingValidator].
        /// </summary>
        public bool IsIncomingValid { get; set; }

        /// <summary>
        /// Gets or sets whether this is a valid command.
        /// When false, the command will not be validated by the [CommandHandlingValidator].
        /// </summary>
        public bool IsHandlingValid { get; set; }
    }

    public class OneHandler : IAutoService
    {
        [CommandHandler]
        public string Execute( ITestCommand cmd, CurrentCultureInfo culture )
        {
            return culture.CurrentCulture.Name;
        }

        [IncomingValidator]
        public void IncomingValidate( UserMessageCollector c, ITestCommand cmd, CurrentCultureInfo culture )
        {
            c.Info( $"The collector is '{c.Culture}' The current is '{culture.CurrentCulture}'.", "Test.Info" );
            if( !cmd.IsIncomingValid ) c.Error( $"Sorry, this command is INCOMING invalid!", "Test.InvalidIncomingCommand" );
        }

        [CommandHandlingValidator]
        public void HandlingValidate( UserMessageCollector c, ITestCommand cmd, CurrentCultureInfo culture )
        {
            Throw.DebugAssert( c.CurrentCultureInfo == culture );
            if( !cmd.IsHandlingValid ) c.Error( $"Sorry, this command is HANDLING invalid!", "Test.InvalidHandlingCommand" );
        }
    }


    [Test]
    public async Task command_with_no_current_culture_uses_the_english_default_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( ITestCommand ),
                                              typeof( OneHandler ),
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
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""TestCommand"",{""CurrentCultureName"":null,""IsIncomingValid"":true,""IsHandlingValid"":true}]" );
            string response = await r.Content.ReadAsStringAsync();
            var result = pocoDirectory.Find<ICrisCallResult>()!.ReadJson( response );
            Throw.DebugAssert( result != null && result.ValidationMessages != null );
            result.Result.ShouldBe( "en" );
            result.ValidationMessages.Count.ShouldBe( 2 );
            result.ValidationMessages[0].AsSimpleUserMessage()
                    .ShouldBe( new SimpleUserMessage( UserMessageLevel.Info, "The collector is 'en' The current is 'en'.", 0 ) );
            result.ValidationMessages[1].AsSimpleUserMessage()
                    .ShouldBe( new SimpleUserMessage( UserMessageLevel.Warn, "Culture name is null. It will be ignored.", 0 ) );
        }
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""TestCommand"",{""CurrentCultureName"":null,""IsIncomingValid"":false}]" );
            string response = await r.Content.ReadAsStringAsync();
            var result = pocoDirectory.Find<ICrisCallResult>()!.ReadJson( response );
            Throw.DebugAssert( result != null && result.ValidationMessages != null );
            result.ValidationMessages.Count.ShouldBe( 3 );
            result.ValidationMessages[0].AsSimpleUserMessage()
                    .ShouldBeEquivalentTo( new SimpleUserMessage( UserMessageLevel.Info, "The collector is 'en' The current is 'en'.", 0 ) );
            result.ValidationMessages[1].AsSimpleUserMessage()
                    .ShouldBeEquivalentTo( new SimpleUserMessage( UserMessageLevel.Error, "Sorry, this command is INCOMING invalid!", 0 ) );
            result.ValidationMessages[2].AsSimpleUserMessage()
                    .ShouldBeEquivalentTo( new SimpleUserMessage( UserMessageLevel.Warn, "Culture name is null. It will be ignored.", 0 ) );
            result.Result.ShouldBeAssignableTo<ICrisResultError>();
        }
    }

    [Test]
    public async Task command_with_culture_Async()
    {
        NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr" ).SetCachedTranslations( new[] {
            ("Test.Info", "Le validateur est en '{0}', la culture courante en '{1}'."),
            ("Test.InvalidIncomingCommand", "Désolé, INCOMING invalide."),
            ("Test.InvalidHandlingCommand", "Désolé, HANDLING invalide."),
        } );
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( ITestCommand ),
                                              typeof( OneHandler ),
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
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri,
                    """["TestCommand",{"CurrentCultureName":"fr","IsIncomingValid":true,"IsHandlingValid":true}]""" );

            string response = await r.Content.ReadAsStringAsync();
            var result = pocoDirectory.Find<ICrisCallResult>()!.ReadJson( response );
            Throw.DebugAssert( result != null && result.ValidationMessages != null );
            result.Result.ShouldBe( "fr" );
            result.ValidationMessages.Select( m => m.AsSimpleUserMessage() ).ShouldHaveSingleItem()
                    .ShouldBe( new SimpleUserMessage( UserMessageLevel.Info, "Le validateur est en 'fr', la culture courante en 'en'.", 0 ) );
        }
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri,
                """["TestCommand",{"CurrentCultureName":"fr","IsIncomingValid":false}]""" );
            string response = await r.Content.ReadAsStringAsync();
            var result = pocoDirectory.Find<ICrisCallResult>()!.ReadJson( response );
            Throw.DebugAssert( result != null && result.ValidationMessages != null );
            result.ValidationMessages.Count.ShouldBe( 2 );
            result.ValidationMessages[0].AsSimpleUserMessage()
                .ShouldBe( new SimpleUserMessage( UserMessageLevel.Info, "Le validateur est en 'fr', la culture courante en 'en'.", 0 ) );
            result.ValidationMessages[1].AsSimpleUserMessage()
                    .ShouldBe( new SimpleUserMessage( UserMessageLevel.Error, "Désolé, INCOMING invalide.", 0 ) );
            result.Result.ShouldBeAssignableTo<ICrisResultError>();
            var e = (ICrisResultError)result.Result!;
            e.Errors.ShouldHaveSingleItem().Text.ShouldBe( "Désolé, INCOMING invalide." );
        }
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri,
                """["TestCommand",{"CurrentCultureName":"fr","IsIncomingValid":true,"IsHandlingValid":false}]""" );
            string response = await r.Content.ReadAsStringAsync();
            var result = pocoDirectory.Find<ICrisCallResult>()!.ReadJson( response );
            Throw.DebugAssert( result != null && result.ValidationMessages != null );
            result.ValidationMessages.Count.ShouldBe( 2 );
            result.ValidationMessages[0].AsSimpleUserMessage()
                    .ShouldBe( new SimpleUserMessage( UserMessageLevel.Info, "Le validateur est en 'fr', la culture courante en 'en'.", 0 ) );
            result.ValidationMessages[1].AsSimpleUserMessage()
                    .ShouldBe( new SimpleUserMessage( UserMessageLevel.Error, "Désolé, HANDLING invalide.", 0 ) );
            result.Result.ShouldBeAssignableTo<ICrisResultError>();
            var e = (ICrisResultError)result.Result!;
            e.Errors.ShouldHaveSingleItem().Text.ShouldBe( "Désolé, HANDLING invalide." );
        }
    }
}
