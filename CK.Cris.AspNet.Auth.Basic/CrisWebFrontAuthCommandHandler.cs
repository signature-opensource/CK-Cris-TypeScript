using CK.Auth;
using CK.Core;
using CK.Cris;
using System.Threading.Tasks;

namespace CK.AspNet.Auth.Cris;

/// <summary>
/// Endpoint service that can handle <see cref="IBasicLoginCommand"/>, <see cref="IRefreshAuthenticationCommand"/>
/// and <see cref="ILogoutCommand"/>.
/// </summary>
[SingletonContainerConfiguredService]
public class CrisWebFrontAuthCommandHandler : ISingletonAutoService
{
    readonly WebFrontAuthService _authService;

    /// <summary>
    /// Initializes a new <see cref="CrisWebFrontAuthCommandHandler"/>.
    /// </summary>
    /// <param name="authService">The authentication service.</param>
    public CrisWebFrontAuthCommandHandler( WebFrontAuthService authService )
    {
        _authService = authService;
    }

    /// <summary>
    /// Handles <see cref="IBasicLoginCommand"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="httpContext">The required http context.</param>
    /// <param name="culture">The current culture (used for the <see cref="IStandardResultPart.UserMessages"/>).</param>
    /// <param name="cmd">The command.</param>
    /// <returns>The authentication result.</returns>
    [CommandHandler]
    public async Task<IAuthenticationResult> BasicLoginAsync( IActivityMonitor monitor,
                                                              ScopedHttpContext httpContext,
                                                              CurrentCultureInfo culture,
                                                              IBasicLoginCommand cmd )
    {
        var r = await _authService.BasicLoginCommandAsync( monitor,
                                                           httpContext.HttpContext,
                                                           cmd.UserName,
                                                           cmd.Password,
                                                           cmd.ExpiresTimeSpan,
                                                           cmd.CriticalExpiresTimeSpan,
                                                           cmd.ImpersonateActualUser );
        IAuthenticationResult result = cmd.CreateResult();
        if( r.Success )
        {
            result.Info.InitializeFrom( r.Info );
            result.Token = r.Token;
        }
        else
        {
            result.UserMessages.Add( UserMessage.Create( culture, UserMessageLevel.Error, r.ErrorId ) );
            if( !string.IsNullOrEmpty( r.ErrorText ) )
            {
                result.UserMessages.Add( UserMessage.Create( culture, UserMessageLevel.Error, r.ErrorText ) );
            }
        }
        return result;
    }

    /// <summary>
    /// Handles <see cref="IRefreshAuthenticationCommand"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="httpContext">The required http context.</param>
    /// <param name="cmd">The command.</param>
    /// <returns>The refreshed authentication result.</returns>
    [CommandHandler]
    public async Task<IAuthenticationResult> RefreshAsync( IActivityMonitor monitor,
                                                           ScopedHttpContext httpContext,
                                                           IRefreshAuthenticationCommand cmd )
    {
        var (info, token) = await _authService.RefreshCommandAsync( monitor,
                                                        httpContext.HttpContext,
                                                        cmd.CallBackend );
        return cmd.CreateResult( result =>
        {
            result.Info.InitializeFrom( info );
            result.Token = token;
        } );
    }

    /// <summary>
    /// Handles the <see cref="ILogoutCommand"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="httpContext">The required http context.</param>
    /// <param name="cmd">The command.</param>
    /// <returns>The awaitable.</returns>
    [CommandHandler]
    public Task LogoutAsync( IActivityMonitor monitor,
                             ScopedHttpContext httpContext,
                             ILogoutCommand cmd )
    {
        return _authService.LogoutCommandAsync( monitor, httpContext.HttpContext );
    }

}
