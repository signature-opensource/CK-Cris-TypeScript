using CK.Auth;
using CK.Core;
using CK.IO.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The authentication of the "/.cris" requests. It must run before the Cris middleware (see
/// <see cref="CrisAuthenticationExtensions.UseCrisAuthentication"/>).
/// <list type="number">
///   <item>
///   <b>Same origin:</b> a request whose <c>Sec-Fetch-Site</c> is not "same-origin", or whose <c>Origin</c> is not the
///   request's own origin, is refused (403). This is mandatory, not a defense in depth: the cookies are <c>SameSite=Strict</c>,
///   which a sibling sub-domain bypasses. A request without these headers is not sent by a browser (no cross-site risk).
///   </item>
///   <item>
///   <b>Bearer + binding:</b> the bearer token is read (<see cref="IAuthenticationTokenReader"/>) and accepted only when the
///   request presents the binding secret of its session (the binding cookie). Otherwise the request is anonymous.
///   </item>
///   <item>
///   <b>"/.cris/auth":</b> the route of the session commands (and only them, see <see cref="CrisAspNetAuthenticationPackage"/>). It
///   accepts only POST with a JSON content. A session command is relayed to a remote authentication center when there is one
///   (<see cref="IAuthenticationSessionRelay"/>), executed here otherwise.
///   </item>
/// </list>
/// </summary>
public sealed class CrisAuthenticationMiddleware
{
    static readonly PathString _crisPath = new PathString( "/.cris" );
    static readonly PathString _authPath = new PathString( CrisAspNetAuthenticationPackage.AuthRoute );
    readonly RequestDelegate _next;
    readonly CrisAspNetService _service;
    readonly PocoDirectory _pocoDirectory;

    /// <summary>
    /// Initializes the middleware.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="service">The Cris service.</param>
    /// <param name="pocoDirectory">The poco directory.</param>
    public CrisAuthenticationMiddleware( RequestDelegate next, CrisAspNetService service, PocoDirectory pocoDirectory )
    {
        _next = next;
        _service = service;
        _pocoDirectory = pocoDirectory;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="ctx">The http context.</param>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The awaitable.</returns>
    public async Task InvokeAsync( HttpContext ctx, IActivityMonitor monitor )
    {
        if( !ctx.Request.Path.StartsWithSegments( _crisPath ) )
        {
            await _next( ctx );
            return;
        }
        if( !IsSameOrigin( monitor, ctx.Request ) )
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        var services = ctx.RequestServices;
        var auth = services.GetRequiredService<HttpAuthenticationContext>();
        var endpoint = new HttpSessionEndpoint( ctx );
        auth.HttpEndpoint = endpoint;
        auth.Info = await AuthenticateAsync( monitor, services, endpoint );
        if( ctx.Request.Path == _authPath )
        {
            await HandleSessionCommandAsync( ctx, monitor, services );
            return;
        }
        await _next( ctx );
    }

    static bool IsSameOrigin( IActivityMonitor monitor, HttpRequest request )
    {
        var site = request.Headers["Sec-Fetch-Site"];
        if( site.Count > 0 )
        {
            if( site.Count == 1 && site[0] == "same-origin" ) return true;
            monitor.Warn( ActivityMonitor.Tags.SecurityCritical, $"Request to '{request.Path}' refused: Sec-Fetch-Site is '{site}'." );
            return false;
        }
        var origin = request.Headers.Origin;
        if( origin.Count > 0 )
        {
            var expected = $"{request.Scheme}://{request.Host}";
            if( origin.Count == 1 && string.Equals( origin[0], expected, StringComparison.OrdinalIgnoreCase ) ) return true;
            monitor.Warn( ActivityMonitor.Tags.SecurityCritical, $"Request to '{request.Path}' refused: Origin is '{origin}', expected '{expected}'." );
            return false;
        }
        return true;
    }

    static async Task<AuthenticationInfo> AuthenticateAsync( IActivityMonitor monitor, IServiceProvider services, HttpSessionEndpoint endpoint )
    {
        var token = endpoint.PresentedToken;
        if( token == null ) return AuthenticationInfo.None;
        var reader = services.GetService<IAuthenticationTokenReader>();
        if( reader == null )
        {
            monitor.Warn( "A bearer token is presented but there is no IAuthenticationTokenReader: the request is anonymous." );
            return AuthenticationInfo.None;
        }
        var data = await reader.ReadTokenAsync( monitor, token ).ConfigureAwait( false );
        if( data == null ) return AuthenticationInfo.None;
        var binding = endpoint.PresentedBindingSecret;
        if( binding == null
            || !CryptographicOperations.FixedTimeEquals( Encoding.ASCII.GetBytes( HttpSessionEndpoint.Hash( binding ) ),
                                                         Encoding.ASCII.GetBytes( data.BindingSecretHash ) ) )
        {
            monitor.Warn( ActivityMonitor.Tags.SecurityCritical, "A valid bearer token is presented without the binding secret of its session: the request is anonymous." );
            return AuthenticationInfo.None;
        }
        return data.Info;
    }

    async Task HandleSessionCommandAsync( HttpContext ctx, IActivityMonitor monitor, IServiceProvider services )
    {
        if( !HttpMethods.IsPost( ctx.Request.Method ) )
        {
            ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        if( !CrisMiddleware.IsJsonContent( ctx.Request ) )
        {
            ctx.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        var relay = services.GetService<IAuthenticationSessionRelay>();
        ctx.Response.StatusCode = 200;
        CrisMiddleware.SetJsonResponseHeaders( ctx.Response );
        var (result, typeFilterName) = await _service.HandleRequestAsync( monitor,
                                                                          ctx.Request,
                                                                          CrisAspNetService.StandardReadCommandAsync,
                                                                          executeOverride: relay == null
                                                                                            ? null
                                                                                            : c => c is ICommandAuthenticationSession s ? relay.TryRelay( monitor, s ) : null );
        var writeOptions = _service.CreateJsonExportOptions( ctx.Request, typeFilterName, skipValidation: true );
        if( !_pocoDirectory.WriteJson( ctx.Response.BodyWriter, result, withType: false, writeOptions ) )
        {
            Throw.CKException( $"Poco '{result.GetType().FullName}' has been filtered out by '{typeFilterName}' ExchangeableRuntimeFilter." );
        }
    }
}
