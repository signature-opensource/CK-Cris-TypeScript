using CK.AspNet.Auth;
using CK.Auth;
using CK.Core;
using CK.Testing;
using Shouldly;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace CK.Cris.AspNet.Tests.AuthTests;

[TestFixture]
public class AuthenticatedCommandTests
{
    /// <summary>
    /// An unsafe command: when validated or executed, the <see cref="IAuthenticationInfo.UnsafeUser"/> is known
    /// but <see cref="IAuthenticationInfo.User"/> is the anonymous.
    /// <para>
    /// The <see cref="CrisAuthenticationService"/> automatically ensures that the <see cref="IAuthUnsafePart.ActorId"/>
    /// is the one on the currently connected user otherwise, the command is not validated.
    /// </para>
    /// </summary>
    [ExternalName( "UnsafeCommand" )]
    public interface IUnsafeCommand : ICommandAuthUnsafe
    {
        /// <summary>
        /// Gets or sets a string used by the test. When set to "NO",
        /// it means that the command MUST NOT be validated: the handler
        /// never sees it.
        /// </summary>
        string UserInfo { get; set; }
    }

    /// <summary>
    /// Same as <see cref="IUnsafeCommand"/> but with a result that is list of integers.
    /// </summary>
    [ExternalName( "UnsafeWithResultCommand" )]
    public interface IUnsafeWithResultCommand : ICommand<List<int>>, ICommandAuthUnsafe
    {
        string UserInfo { get; set; }
    }

    /// <summary>
    /// Before reaching this handler, 
    /// </summary>
    public class UnsafeHandler : IAutoService
    {
        [CommandHandler]
        public void Execute( IUnsafeCommand cmd )
        {
            cmd.UserInfo.ShouldNotStartWith( "NO" );
            LastUserInfo = cmd.UserInfo;
        }

        [CommandHandler]
        public List<int> Execute( IUnsafeWithResultCommand cmd )
        {
            cmd.UserInfo.ShouldNotStartWith( "NO" );
            LastUserInfo = cmd.UserInfo;
            return new List<int>() { 42, 3712 };
        }

        static public string? LastUserInfo;
    }

    [Test]
    public async Task ICommandAuthUnsafe_cannot_be_fooled_on_its_ActorId_Async()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( IUnsafeCommand ),
                                              typeof( IUnsafeWithResultCommand ),
                                              typeof( UnsafeHandler ),
                                              typeof( CrisAuthenticationService ),
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

        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""UnsafeCommand"",{""UserInfo"":""YES"",""ActorId"":0}]" );
            Throw.DebugAssert( r != null );
            var result = await pocoDirectory.GetCrisResultWithCorrelationIdSetToNullAsync( r );
            result.ToString().ShouldBe( @"{""Result"":null,""ValidationMessages"":null,""CorrelationId"":null}" );
        }
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""UnsafeWithResultCommand"",{""UserInfo"":""YES. There is no ActorId in the Json => it is let to null.""}]" );
            Throw.DebugAssert( r != null );
            await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Invalid property: ActorId cannot be null." ) );
        }
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""UnsafeCommand"",{""UserInfo"":""NO WAY!"",""ActorId"":3712}]" );
            Throw.DebugAssert( r != null );
            var result = await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Invalid actor identifier: the provided identifier doesn't match the current authentication." ) );
            var correlationId = ActivityMonitor.Token.Parse( result.CorrelationId );
            string.IsNullOrWhiteSpace( correlationId.OriginatorId ).ShouldBeFalse();
            var errorLogKey = ActivityMonitor.LogKey.Parse( ((ICrisResultError)result.Result!).LogKey );
            string.IsNullOrWhiteSpace( errorLogKey.OriginatorId ).ShouldBeFalse();
        }
        UnsafeHandler.LastUserInfo = null;
        await client.AuthenticationBasicLoginAsync( "Albert", true );
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""UnsafeCommand"",{""userInfo"":""Yes! Albert 3712 is logged in."",""actorId"":3712}]" );
            Throw.DebugAssert( r != null );
            var result = await pocoDirectory.GetCrisResultWithCorrelationIdSetToNullAsync( r );
            result.ToString().ShouldBe( @"{""Result"":null,""ValidationMessages"":null,""CorrelationId"":null}" );
            UnsafeHandler.LastUserInfo.ShouldBe( "Yes! Albert 3712 is logged in." );
        }
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""UnsafeCommand"",{""UserInfo"":""NO WAY!"",""ActorId"":7}]" );
            Throw.DebugAssert( r != null );
            await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Invalid actor identifier: the provided identifier doesn't match the current authentication." ) );
        }
        await client.AuthenticationLogoutAsync();
        {
            HttpResponseMessage? r = await client.PostJsonAsync( LocalHelper.CrisUri, @"[""UnsafeCommand"",{""UserInfo"":""NO! Albert is no more here."",""ActorId"":3712}]" );
            Throw.DebugAssert( r != null );
            await pocoDirectory.GetValidationErrorsAsync( r, new SimpleUserMessage( UserMessageLevel.Error, "Invalid actor identifier: the provided identifier doesn't match the current authentication." ) );
        }
    }

}
