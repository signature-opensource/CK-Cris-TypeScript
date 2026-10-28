using CK.AppIdentity;
using CK.Auth;
using CK.Core;
using CK.IO.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
///   accepts only POST with a JSON content. A session command is always executed here, by this endpoint (what it needs from
///   the authentication center is an operation, executed or dispatched by CK.Cris.Authentication).
///   </item>
/// </list>
/// </summary>
public sealed class CrisAuthenticationMiddleware
{
    static readonly PathString _crisPath = new PathString( "/.cris" );
    static readonly PathString _authPath = new PathString( CrisAspNetAuthenticationPackage.AuthRoute );
    static readonly PathString _callbackPath = new PathString( CrisAuthenticationCookies.RemoteLoginCallbackPath );
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
        if( ctx.Request.Path == _callbackPath )
        {
            await HandleRemoteLoginCallbackAsync( ctx, monitor );
            return;
        }
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
        auth.Info = (await AuthenticateAsync( monitor, services, endpoint )).WithCallerParty( GetCallerPartyId( services ) );
        if( ctx.Request.Path == _authPath )
        {
            await HandleCommandAsync( ctx, monitor, c => OverrideSessionCommand( monitor, c, endpoint ) );
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

    // A request is received directly: the party through which the identity entered is our local party. Its PartyActorId is
    // 0 when the party model is not installed or when it is not resolved yet. The party model is installed when there is an
    // IApplicationIdentityService (referencing CK.AppIdentity.Abstractions registers the CurrentLocalParty, but its default
    // value then cannot be resolved without it).
    static int GetCallerPartyId( IServiceProvider services )
    {
        return services.GetService<IApplicationIdentityService>() != null
                ? services.GetRequiredService<CurrentLocalParty>().LocalParty.PartyActorId
                : 0;
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

    // The routes handled here: POST with a JSON content, the execution may be overridden (null: executed here).
    async Task HandleCommandAsync( HttpContext ctx, IActivityMonitor monitor, Func<IAbstractCommand, Task<object?>?> executeOverride )
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
        ctx.Response.StatusCode = 200;
        CrisMiddleware.SetJsonResponseHeaders( ctx.Response );
        var (result, typeFilterName) = await _service.HandleRequestAsync( monitor,
                                                                          ctx.Request,
                                                                          CrisAspNetService.StandardReadCommandAsync,
                                                                          executeOverride: executeOverride );
        var writeOptions = _service.CreateJsonExportOptions( ctx.Request, typeFilterName, skipValidation: true );
        if( !_pocoDirectory.WriteJson( ctx.Response.BodyWriter, result, withType: false, writeOptions ) )
        {
            Throw.CKException( $"Poco '{result.GetType().FullName}' has been filtered out by '{typeFilterName}' ExchangeableRuntimeFilter." );
        }
    }

    // A remote login callback is built by the callback endpoint only: a client never sends it.
    // A starting remote login records how it ends (see HttpSessionEndpoint.PrepareRemoteLogin). Then executed here (null).
    Task<object?>? OverrideSessionCommand( IActivityMonitor monitor, IAbstractCommand c, HttpSessionEndpoint endpoint )
    {
        if( c is IRemoteLoginCallbackCommand )
        {
            monitor.Warn( ActivityMonitor.Tags.SecurityCritical, "An IRemoteLoginCallbackCommand has been sent to '/.cris/auth': it is refused." );
            return Task.FromResult<object?>( _pocoDirectory.Create<ICrisResultError>( e => e.Errors.Add( UserMessage.Error( NormalizedCultureInfo.CodeDefault,
                                                                                                                             "This command is built by the remote login callback only.",
                                                                                                                             "Authentication.InvalidCommand" ) ) ) );
        }
        if( c is IStartLoginCommand start ) endpoint.SetRemoteLoginEnd( start.InlineMode, start.ReturnUrl );
        return null;
    }

    /// <summary>
    /// The query parameter added to the return URL of an inline remote login: "ok", or the resource name of the error.
    /// The client refreshes its session when it finds it (and removes it).
    /// </summary>
    public const string RemoteLoginMarker = "ck-auth-login";

    // The GET to which the provider redirects the browser. It executes the IRemoteLoginCallbackCommand, deletes the
    // remote login cookie, and ends the login: a popup page that posts { type, success, error } to its own origin (no token),
    // or a redirection to the return URL of an inline login, with the marker. The response never carries a token: the client
    // refreshes its session with the cookies set here.
    async Task HandleRemoteLoginCallbackAsync( HttpContext ctx, IActivityMonitor monitor )
    {
        var response = ctx.Response;
        response.Headers.CacheControl = "no-store";
        // The URL holds the code: it must not leak to the next page.
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        if( !HttpMethods.IsGet( ctx.Request.Method ) )
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        var services = ctx.RequestServices;
        var auth = services.GetRequiredService<HttpAuthenticationContext>();
        var endpoint = new HttpSessionEndpoint( ctx );
        // A navigation carries no bearer: the request is anonymous.
        auth.HttpEndpoint = endpoint;
        auth.Info = AuthenticationInfo.None.WithCallerParty( GetCallerPartyId( services ) );
        var presented = endpoint.PresentedRemoteLogin;
        endpoint.ClearRemoteLogin();
        var q = ctx.Request.Query;
        var command = _pocoDirectory.Create<IRemoteLoginCallbackCommand>( c =>
        {
            c.State = q["state"].ToString();
            c.Code = q["code"].ToString();
            c.Error = q.ContainsKey( "error" ) ? q["error"].ToString() : null;
        } );
        object? result;
        try
        {
            result = await ExecuteAsync( monitor, services, command ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( "While completing a remote login.", ex );
            result = null;
        }
        string? error = null;
        if( result is not ISessionResult r || !r.Success )
        {
            // The resource name of the first error (it is not a secret: "Authentication.RemoteLoginCorrelation"...).
            var resName = (result as ISessionResult)?.UserMessages.Where( m => m.Level == UserMessageLevel.Error ).Select( m => m.ResName ).FirstOrDefault();
            error = string.IsNullOrEmpty( resName ) ? "Authentication.RemoteLoginFailed" : resName;
        }
        if( presented is { Inline: true } p )
        {
            var returnUrl = p.ReturnUrl != null && IsRelativeUrl( p.ReturnUrl ) ? p.ReturnUrl : "/";
            var target = returnUrl + (returnUrl.Contains( '?' ) ? '&' : '?') + RemoteLoginMarker + "=" + Uri.EscapeDataString( error ?? "ok" );
            response.StatusCode = StatusCodes.Status303SeeOther;
            response.Headers.Location = ctx.Request.PathBase + target;
            return;
        }
        var nonce = Base64Url.EncodeToString( RandomNumberGenerator.GetBytes( 16 ) );
        var message = JsonSerializer.Serialize( new Dictionary<string, object?> { ["type"] = "ck-auth-remote-login", ["success"] = error == null, ["error"] = error } );
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{nonce}'";
        await response.WriteAsync( $$"""
                                     <!DOCTYPE html>
                                     <html><head><meta charset="utf-8"><title>Login</title></head><body>
                                     <script nonce="{{nonce}}">
                                     if( window.opener ) window.opener.postMessage( {{message}}, window.location.origin );
                                     window.close();
                                     </script>
                                     </body></html>
                                     """ ).ConfigureAwait( false );
    }

    // The callback command is executed here, like any received command: validated first, with the ambient services its
    // incoming validation configures (executed inline in a scope built from them).
    static async Task<object?> ExecuteAsync( IActivityMonitor monitor, IServiceProvider services, IRemoteLoginCallbackCommand command )
    {
        var validation = await services.GetRequiredService<RawCrisReceiver>().IncomingValidateAsync( monitor, services, command ).ConfigureAwait( false );
        if( !validation.Success )
        {
            monitor.Warn( $"The remote login callback is invalid: {validation.ValidationMessages.Select( m => m.Text ).Concatenate()}" );
            return null;
        }
        var context = services.GetRequiredService<CrisExecutionContext>();
        IExecutedCommand executed = validation.AmbientServiceHub is { } hub
                        ? await context.ExecuteRootCommandInlineAsync( hub, command ).ConfigureAwait( false )
                        : await context.ExecuteRootCommandAsync( (IAbstractCommand)command ).ConfigureAwait( false );
        return executed.Result;
    }

    // A relative path of this origin: a single leading '/' (not "//" nor "/\"), no backslash nor control character.
    static bool IsRelativeUrl( string url )
    {
        if( url.Length == 0 || url[0] != '/' ) return false;
        if( url.Length > 1 && (url[1] == '/' || url[1] == '\\') ) return false;
        return !url.Any( ch => ch == '\\' || char.IsControl( ch ) );
    }
}
