using CK.AppIdentity;
using CK.AspNet.Auth;
using CK.AspNet.Auth.Cris;
using CK.Auth;
using CK.Core;
using CK.Cris.AmbientValues;
using CK.Cris.AspNet;
using CK.Testing;
using Shouldly;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.HttpSender.Tests;




[TestFixture]
public class SenderTests
{
    [Test]
    public async Task sending_commands_Async()
    {
        // We need the fr culture for this test.
        NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr" );
        var serverEngineConfiguration = TestHelper.CreateDefaultEngineConfiguration();
        serverEngineConfiguration.FirstBinPath.Types.Add( typeof( IAuthenticationInfo ),
                                                          typeof( StdAuthenticationTypeSystem ),
                                                          typeof( AuthenticationInfoTokenService ),
                                                          typeof( CrisAuthenticationService ),
                                                          typeof( IBeautifulWithOptionsCommand ),
                                                          typeof( INakedCommand ),
                                                          typeof( AmbientValuesService ),
                                                          typeof( ColorAndNakedService ),
                                                          typeof( WithOptionsService ),
                                                          typeof( ITotalCommand ),
                                                          typeof( ITotalResult ),
                                                          typeof( TotalCommandService ),
                                                          typeof( IBasicLoginCommand ),
                                                          typeof( ILogoutCommand ),
                                                          typeof( IRefreshAuthenticationCommand ),
                                                          typeof( IAuthenticationResult ),
                                                          typeof( IPocoAuthenticationInfo ),
                                                          typeof( IPocoUserInfo ),
                                                          typeof( CrisAspNetService ),
                                                          typeof( CrisWebFrontAuthCommandHandler ) );

        var serverMap = (await serverEngineConfiguration.RunSuccessfullyAsync()).LoadMap();
        var serverBuilder = WebApplication.CreateSlimBuilder();
        await using var runningServer = await serverBuilder.CreateRunningAspNetAuthenticationServerAsync( serverMap, configureApplication: app => app.UseMiddleware<CrisMiddleware>() );

        var serverAddress = runningServer.ServerAddress;

        var callerEngineConfiguration = TestHelper.CreateDefaultEngineConfiguration();
        callerEngineConfiguration.FirstBinPath.Types.Add( typeof( IBeautifulWithOptionsCommand ),
                                                          typeof( INakedCommand ),
                                                          typeof( ITotalCommand ),
                                                          typeof( ITotalResult ),
                                                          typeof( IBasicLoginCommand ),
                                                          typeof( ILogoutCommand ),
                                                          typeof( IRefreshAuthenticationCommand ),
                                                          typeof( IAuthenticationResult ),
                                                          typeof( IPocoAuthenticationInfo ),
                                                          typeof( IPocoUserInfo ),
                                                          typeof( CrisDirectory ),
                                                          typeof( CommonPocoJsonSupport ),
                                                          typeof( ApplicationIdentityService ),
                                                          typeof( CrisHttpSenderFeatureDriver ) );

        var callerMap = (await callerEngineConfiguration.RunSuccessfullyAsync()).LoadMap();
        using var runningCaller = await LocalHelper.CreateRunningCallerAsync( callerMap, serverAddress );

        var callerPoco = runningCaller.Services.GetRequiredService<PocoDirectory>();
        var sender = runningCaller.Services.GetRequiredService<ApplicationIdentityService>().Remotes
                                                .Single( r => r.PartyName == "$Server" )
                                                .GetRequiredFeature<CrisHttpSender>();

        // ITotalCommand requires Normal authentication. 
        var totalCommand = callerPoco.Create<ITotalCommand>();
        // We don't have the AmbientValues here to apply them.
        // ActorId is set to its default 0 (this would have been the default value).
        totalCommand.ActorId = 0;
        var totalExecutedCommand = await sender.SendAsync( TestHelper.Monitor, totalCommand );
        totalExecutedCommand.Result.ShouldNotBeNull().ShouldBeAssignableTo<ICrisResultError>();
        var error = (ICrisResultError)totalExecutedCommand.Result!;
        error.IsValidationError.ShouldBeTrue();
        error.Errors[0].Text.ShouldStartWith( "Invalid authentication level: " );

        var loginCommand = callerPoco.Create<IBasicLoginCommand>( c =>
        {
            c.UserName = "Albert";
            c.Password = "success";
        } );

        var loginAlbert = await sender.SendAndGetResultOrThrowAsync( TestHelper.Monitor, loginCommand );
        loginAlbert.Info.User.UserName.ShouldBe( "Albert" );

        // Unexisting user id.
        totalCommand.ActorId = 9999999;
        totalExecutedCommand = await sender.SendAsync( TestHelper.Monitor, totalCommand );
        totalExecutedCommand.Result.ShouldNotBeNull().ShouldBeAssignableTo<ICrisResultError>();
        error = (ICrisResultError)totalExecutedCommand.Result!;
        error.IsValidationError.ShouldBeTrue();
        error.Errors[0].Text.ShouldStartWith( "Invalid actor identifier: " );

        // Albert (null current culture name): this is executed in the Global DI context.
        totalCommand.ActorId = 3712;
        var totalResult = await sender.SendAndGetResultOrThrowAsync( TestHelper.Monitor, totalCommand );
        totalResult.Success.ShouldBeTrue();
        totalResult.ActorId.ShouldBe( 3712 );
        totalResult.CultureName.ShouldBe( "en" );

        // Albert in French: this is executed in a Background job. 
        totalCommand.CurrentCultureName = "fr";
        totalResult = await sender.SendAndGetResultOrThrowAsync( TestHelper.Monitor, totalCommand );
        totalResult.Success.ShouldBeTrue();
        totalResult.ActorId.ShouldBe( 3712, "The authentication info has been transferred." );
        totalResult.CultureName.ShouldBe( "fr", "The current culture is French." );

        // Albert in French sends an invalid action.
        totalCommand.Action = "Invalid";
        totalExecutedCommand = await sender.SendAsync( TestHelper.Monitor, totalCommand );
        totalExecutedCommand.Result.ShouldNotBeNull().ShouldBeAssignableTo<ICrisResultError>();
        error = (ICrisResultError)totalExecutedCommand.Result!;
        error.IsValidationError.ShouldBeTrue();
        error.Errors[0].Text.ShouldStartWith( "The Action must be Bug!, Error!, Warn! or empty. Not 'Invalid'." );

        // Logout.
        await sender.SendOrThrowAsync( TestHelper.Monitor, callerPoco.Create<ILogoutCommand>() );

        await TestSimpleCommandsAsync( callerPoco, sender );

        await TestAuthenticationCommandsAsync( callerPoco, sender );

    }

    static async Task TestAuthenticationCommandsAsync( PocoDirectory callerPoco, CrisHttpSender sender )
    {
        sender.AuthorizationToken.ShouldBeNull( "No authentication token." );

        var loginCommand = callerPoco.Create<IBasicLoginCommand>( c =>
        {
            c.UserName = "Albert";
            c.Password = "success";
        } );

        var initialAuth = await sender.SendAndGetResultOrThrowAsync( TestHelper.Monitor, loginCommand );
        initialAuth.Success.ShouldBeTrue();
        initialAuth.Info.Level.ShouldBe( AuthLevel.Normal );
        initialAuth.Info.User.UserName.ShouldBe( "Albert" );
        sender.AuthorizationToken.ShouldNotBeNull( "The AuthorizationToken is set." );

        var refreshCommand = callerPoco.Create<IRefreshAuthenticationCommand>();
        var refreshedAuth = await sender.SendAndGetResultOrThrowAsync( TestHelper.Monitor, refreshCommand );
        refreshedAuth.Success.ShouldBeTrue();
        refreshedAuth.Info.User.UserName.ShouldBe( "Albert" );

        var logoutCommand = callerPoco.Create<ILogoutCommand>();
        await sender.SendOrThrowAsync( TestHelper.Monitor, logoutCommand );
        sender.AuthorizationToken.ShouldBeNull( "No more AuthorizationToken." );
    }

    static async Task TestSimpleCommandsAsync( PocoDirectory callerPoco, CrisHttpSender sender )
    {
        // Command with result.
        var cmd = callerPoco.Create<IBeautifulCommand>( c =>
        {
            c.Color = "Black";
            c.Beauty = "Marvellous";
        } );
        var result = await sender.SendAsync( TestHelper.Monitor, cmd );
        result.Result.ShouldBe( "Black - Marvellous - 0" );

        // Command without result.
        var naked = callerPoco.Create<INakedCommand>( c => c.Event = "Something" );
        var nakedResult = await sender.SendAsync( TestHelper.Monitor, naked );
        nakedResult.Result.ShouldBeNull();

        // Command without result that throws.
        var nakedBug = callerPoco.Create<INakedCommand>( c => c.Event = "Bug!" );
        var nakedBugResult = await sender.SendAsync( TestHelper.Monitor, nakedBug );
        nakedBugResult.Result.ShouldNotBeNull().ShouldBeAssignableTo<ICrisResultError>();

        // Command without result that throws and use SendOrThrowAsync.
        var nakedBug2 = callerPoco.Create<INakedCommand>( c => c.Event = "Bug!" );
        await Util.Awaitable( () => sender.SendOrThrowAsync( TestHelper.Monitor, nakedBug2 ) )
            .ShouldThrowAsync<CKException>();
        //
        // Why does FluentAssertions now fails to match this?
        // It used to work and this is still correct :(.
        //
        //   .WithMessage( """
        //   - An unhandled error occurred while executing command 'CK.Cris.HttpSender.Tests.INakedCommand' (LogKey: *).
        //     -> *SenderTests.cs@*
        //   - Outer exception.
        //     - One or more errors occurred.
        //       - Bug! (n°1)
        //       - Bug! (n°2)
        //   """ );
    }

    [Test]
    public async Task retry_strategy_Async()
    {
        var callerEngineConfiguration = TestHelper.CreateDefaultEngineConfiguration();
        callerEngineConfiguration.FirstBinPath.Types.Add( typeof( IBeautifulWithOptionsCommand ),
                                                          typeof( CrisDirectory ),
                                                          typeof( CommonPocoJsonSupport ),
                                                          typeof( ApplicationIdentityService ),
                                                          typeof( ApplicationIdentityServiceConfiguration ),
                                                          typeof( CrisHttpSenderFeatureDriver ) );
        var map = (await callerEngineConfiguration.RunSuccessfullyAsync()).LoadMap();

        // The serverAddress http://[::1]:65036/ has no running server.
        using var runningCaller = await LocalHelper.CreateRunningCallerAsync( map, "http://[::1]:65036/" );
        var callerPoco = runningCaller.Services.GetRequiredService<PocoDirectory>();
        var sender = runningCaller.Services.GetRequiredService<ApplicationIdentityService>().Remotes
                                            .Single( r => r.PartyName == "$Server" )
                                            .GetRequiredFeature<CrisHttpSender>();
        var cmd = callerPoco.Create<IBeautifulCommand>( c =>
        {
            c.Color = "Black";
            c.Beauty = "Marvellous";
        } );

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            var result = await sender.SendAsync( TestHelper.Monitor, cmd );
            logs.ShouldContain( """Sending ["CK.Cris.HttpSender.Tests.IBeautifulCommand",{"beauty":"Marvellous","waitTime":0,"color":"Black"}] to 'Domain/$Server/#Dev'.""" )
                .ShouldContain( """Request failed on 'Domain/$Server/#Dev' (attempt n°0).""" )
                .ShouldContain( """Request failed on 'Domain/$Server/#Dev' (attempt n°1).""" )
                .ShouldContain( """Request failed on 'Domain/$Server/#Dev' (attempt n°2).""" )
                .ShouldContain( """While sending: ["CK.Cris.HttpSender.Tests.IBeautifulCommand",{"beauty":"Marvellous","waitTime":0,"color":"Black"}]""" );
        }
    }

}
