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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Cris.HttpSender.Tests;


public interface IFullCommand : ICommand, ICommandAuthNormal, ICommandAuthImpersonation, ICommandAuthDeviceId, ICommandCurrentCulture
{
    string Prefix { get; set; }
}

public sealed class FullCommandService : ISingletonAutoService
{
    static DateTime _start;
    static long GetDeltaMS() => (DateTime.UtcNow - _start).Ticks / TimeSpan.TicksPerMillisecond;
    static readonly List<string> _messages = new List<string>();
    public static void Start()
    {
        _start = DateTime.UtcNow;
        _messages.Clear();
    }
    public static void ClearMessage()
    {
        lock( _messages ) _messages.Clear();
    }
    public static void AddMessage( string message )
    {
        lock( _messages ) _messages.Add( message );
    }
    public static int MessageCount
    {
        get
        {
            lock( _messages ) return _messages.Count;
        }
    }

    public static string MessageAt( int i )
    {
        lock( _messages ) return _messages[i];
    }

    [CommandHandler]
    public void Handle( IFullCommand c, IAuthenticationInfo auth, CurrentCultureInfo culture )
    {
        AddMessage( $"{c.Prefix}-{auth.User.UserName}-{auth.ActualUser.UserName}-{auth.DeviceId.Length}-{culture.CurrentCulture.Name}-{GetDeltaMS()}" );
    }

    [RoutedEventHandler]
    public void OnDelayedCommandExecuted( IDelayedCommandExecutedEvent e )
    {
        if( e.Command is IFullCommand c )
        {
            AddMessage( $"EVENT: {c.Prefix}" );
        }
    }
}

[TestFixture]
public class DelayedCommandTests
{
    // When this one starts a cold test, it pays the price of loading all the assemblies
    // and this takes time (especially in Debug).
    [CancelAfter( 25000 )]
    [Test]
    public async Task simple_delayed_command_Async( CancellationToken cancellation )
    {
        // We need the fr culture for this test.
        NormalizedCultureInfo.EnsureNormalizedCultureInfo( "fr" );

        var serverEngineConfiguration = TestHelper.CreateDefaultEngineConfiguration();
        serverEngineConfiguration.FirstBinPath.Types.Add( typeof( IAuthenticationInfo ),
                                                          typeof( CrisAuthenticationService ),
                                                          typeof( AmbientValuesService ),
                                                          typeof( StdAuthenticationTypeSystem ),
                                                          typeof( AuthenticationInfoTokenService ),
                                                          typeof( IDelayedCommand ),
                                                          typeof( CrisDelayedCommandService ),
                                                          typeof( CrisAspNetService ),
                                                          typeof( IDelayedCommandExecutedEvent ),
                                                          typeof( IFullCommand ),
                                                          typeof( FullCommandService ),
                                                          typeof( IBasicLoginCommand ),
                                                          typeof( ILogoutCommand ),
                                                          typeof( IRefreshAuthenticationCommand ),
                                                          typeof( IAuthenticationResult ),
                                                          typeof( IPocoAuthenticationInfo ),
                                                          typeof( IPocoUserInfo ),
                                                          typeof( CrisWebFrontAuthCommandHandler ) );
        var serverMap = (await serverEngineConfiguration.RunSuccessfullyAsync()).LoadMap();
        var serverBuilder = WebApplication.CreateSlimBuilder();
        await using var runningServer = await serverBuilder.CreateRunningAspNetAuthenticationServerAsync( serverMap, configureApplication: app => app.UseMiddleware<CrisMiddleware>() );

        var serverAddress = runningServer.ServerAddress;
        var callerConf = TestHelper.CreateDefaultEngineConfiguration();
        callerConf.FirstBinPath.Types.Add( typeof( IDelayedCommand ),
                                           typeof( IFullCommand ),
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
        var callerMap = (await callerConf.RunSuccessfullyAsync()).LoadMap();

        using var runningCaller = await LocalHelper.CreateRunningCallerAsync( callerMap, serverAddress, cancellation );

        var callerPoco = runningCaller.Services.GetRequiredService<PocoDirectory>();
        var sender = runningCaller.Services.GetRequiredService<ApplicationIdentityService>().Remotes
                                                .Single( r => r.PartyName == "$Server" )
                                                .GetRequiredFeature<CrisHttpSender>();

        FullCommandService.Start();

        // Login Albert.
        var loginCommand = callerPoco.Create<IBasicLoginCommand>( c =>
        {
            c.UserName = "Albert";
            c.Password = "success";
        } );
        var loginAlbert = await sender.SendAndGetResultOrThrowAsync( TestHelper.Monitor, loginCommand, cancellationToken: cancellation );
        loginAlbert.Info.User.UserName.ShouldBe( "Albert" );

        // IFullCommand requires Normal authentication. 
        // Using Albert (2) and the device identifier.
        var fullCommand = callerPoco.Create<IFullCommand>();
        fullCommand.Prefix = "n°1";
        fullCommand.ActorId = loginAlbert.Info.User.UserId;
        fullCommand.ActualActorId = loginAlbert.Info.ActualUser.UserId;
        fullCommand.DeviceId = loginAlbert.Info.DeviceId;

        // Baseline: Albert (null current culture name): this is executed in the Global DI context.
        await sender.SendOrThrowAsync( TestHelper.Monitor, fullCommand, cancellationToken: cancellation );
        FullCommandService.MessageAt( 0 ).ShouldMatch( "n°1-Albert-Albert-22-en-.*" );

        // Delayed command now.
        var delayed = callerPoco.Create<IDelayedCommand>();
        delayed.Command = fullCommand;
        delayed.ExecutionDate = DateTime.UtcNow.AddMilliseconds( 150 );

        FullCommandService.ClearMessage();
        await sender.SendOrThrowAsync( TestHelper.Monitor, delayed, cancellationToken: cancellation );
        while( FullCommandService.MessageCount < 2 )
        {
            await Task.Delay( 50, cancellation );
        }
        FullCommandService.MessageAt( 0 ).ShouldMatch( "n°1-Albert-Albert-22-en-.*" );
        FullCommandService.MessageAt( 1 ).ShouldBe( "EVENT: n°1" );

        delayed.ExecutionDate = DateTime.UtcNow.AddMilliseconds( 150 );
        fullCommand.DeviceId = "not-the-device-id";
        var executedCommand = await sender.SendAsync( TestHelper.Monitor, delayed, cancellationToken: cancellation );

        var error = executedCommand.Result as ICrisResultError;
        Throw.DebugAssert( error is not null );
        error = (ICrisResultError)executedCommand.Result!;
        error.IsValidationError.ShouldBeTrue();
        error.Errors[0].Text.ShouldStartWith( "Invalid device identifier: " );

        // Logout.
        await sender.SendOrThrowAsync( TestHelper.Monitor, callerPoco.Create<ILogoutCommand>(), cancellationToken: cancellation );

        // No more allowed.
        delayed.ExecutionDate = DateTime.UtcNow.AddMilliseconds( 150 );
        executedCommand = await sender.SendAsync( TestHelper.Monitor, delayed, cancellationToken: cancellation );
        error = executedCommand.Result as ICrisResultError;
        Throw.DebugAssert( error is not null );
        error = (ICrisResultError)executedCommand.Result!;
        error.IsValidationError.ShouldBeTrue();
        error.Errors[0].Text.ShouldStartWith( "Invalid actor identifier: " );

    }
}
