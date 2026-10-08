using CK.Auth;
using CK.Core;
using Microsoft.AspNetCore.Http;
using System;
using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The session side of the HTTP front (<see cref="IAuthenticationSessionEndpoint"/>): the session secrets are in two cookies.
/// <list type="bullet">
///   <item>
///   The <b>binding</b> cookie (<see cref="BindingCookieName"/>, <c>Path=/.cris</c>) holds the binding secret: every
///   authenticated request presents it, and a bearer token is accepted only with the binding secret of its session.
///   </item>
///   <item>
///   The <b>refresh</b> cookie (<see cref="RefreshCookieName"/>, <c>Path=/.cris/auth</c>) holds the session key and the
///   refresh secret: only the session commands see it.
///   </item>
/// </list>
/// Both are <c>HttpOnly</c>, <c>SameSite=Strict</c>, host-only and <c>Secure</c> (except over http on a loopback address,
/// for the development). They are persistent when the session is remembered, session cookies otherwise.
/// <para>
/// The secrets are generated here and never leave this endpoint: the authentication center only knows their hashes
/// (base64url SHA-256 of the secret).
/// </para>
/// <para>
/// This is not an automatic service: the endpoint of a request is created by the <see cref="CrisAuthenticationMiddleware"/>
/// and provided by the <see cref="HttpAuthenticationContext"/> (see <see cref="Microsoft.Extensions.DependencyInjection.CrisAuthenticationExtensions.AddCrisAuthentication"/>).
/// </para>
/// </summary>
[ExcludedCKType]
public sealed class HttpSessionEndpoint : IAuthenticationSessionEndpoint
{
    /// <summary>
    /// The name of the binding cookie.
    /// </summary>
    public const string BindingCookieName = "ck-auth-binding";

    /// <summary>
    /// The name of the refresh cookie.
    /// </summary>
    public const string RefreshCookieName = "ck-auth-refresh";

    /// <summary>
    /// The path of the binding cookie.
    /// </summary>
    public const string BindingCookiePath = "/.cris";

    /// <summary>
    /// The path of the refresh cookie.
    /// </summary>
    public const string RefreshCookiePath = "/.cris/auth";

    const char _keySeparator = '~';

    readonly HttpContext _context;
    (string Refresh, string Binding)? _prepared;

    /// <summary>
    /// Initializes a new endpoint for a request.
    /// </summary>
    /// <param name="context">The http context.</param>
    public HttpSessionEndpoint( HttpContext context )
    {
        _context = context;
    }

    /// <summary>
    /// Computes the hash of a secret: the base64url SHA-256 of its ASCII bytes.
    /// </summary>
    /// <param name="secret">The secret.</param>
    /// <returns>Its hash.</returns>
    public static string Hash( string secret ) => Base64Url.EncodeToString( SHA256.HashData( Encoding.ASCII.GetBytes( secret ) ) );

    /// <summary>
    /// Gets the binding secret presented by the caller (the binding cookie). Null if none.
    /// </summary>
    public string? PresentedBindingSecret => _context.Request.Cookies[BindingCookieName];

    /// <inheritdoc />
    public AuthenticationSessionProof? PresentedProof
    {
        get
        {
            var binding = PresentedBindingSecret;
            var refresh = _context.Request.Cookies[RefreshCookieName];
            if( string.IsNullOrEmpty( binding ) || string.IsNullOrEmpty( refresh ) ) return null;
            int sep = refresh.LastIndexOf( _keySeparator );
            if( sep <= 0 || sep == refresh.Length - 1 ) return null;
            return new AuthenticationSessionProof( refresh.Substring( 0, sep ), Hash( refresh.Substring( sep + 1 ) ), Hash( binding ) );
        }
    }

    /// <inheritdoc />
    public string? PresentedToken
    {
        get
        {
            var h = _context.Request.Headers.Authorization;
            if( h.Count != 1 ) return null;
            var v = h[0];
            return v != null && v.StartsWith( "Bearer ", StringComparison.Ordinal ) && v.Length > 7 ? v.Substring( 7 ) : null;
        }
    }

    static string NewSecret() => Base64Url.EncodeToString( RandomNumberGenerator.GetBytes( 32 ) );

    /// <inheritdoc />
    public (string RefreshSecretHash, string BindingSecretHash) PrepareSecrets()
    {
        var p = (NewSecret(), NewSecret());
        _prepared = p;
        return (Hash( p.Item1 ), Hash( p.Item2 ));
    }

    /// <inheritdoc />
    public AuthenticationSessionProof Issue( string sessionKey, bool rememberMe, DateTime refreshExpires )
    {
        Throw.CheckNotNullOrEmptyArgument( sessionKey );
        Throw.CheckArgument( sessionKey.IndexOf( _keySeparator ) < 0 );
        var (refresh, binding) = _prepared ?? (NewSecret(), NewSecret());
        _prepared = null;
        DateTimeOffset? expires = rememberMe ? new DateTimeOffset( DateTime.SpecifyKind( refreshExpires, DateTimeKind.Utc ) ) : null;
        _context.Response.Cookies.Append( BindingCookieName, binding, CreateOptions( BindingCookiePath, expires ) );
        _context.Response.Cookies.Append( RefreshCookieName, sessionKey + _keySeparator + refresh, CreateOptions( RefreshCookiePath, expires ) );
        return new AuthenticationSessionProof( sessionKey, Hash( refresh ), Hash( binding ) );
    }

    /// <inheritdoc />
    public void Clear()
    {
        _prepared = null;
        _context.Response.Cookies.Delete( BindingCookieName, CreateOptions( BindingCookiePath, null ) );
        _context.Response.Cookies.Delete( RefreshCookieName, CreateOptions( RefreshCookiePath, null ) );
    }

    CookieOptions CreateOptions( string path, DateTimeOffset? expires )
    {
        return new CookieOptions
        {
            Path = path,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = _context.Request.IsHttps || !IsLoopback( _context ),
            Expires = expires,
            IsEssential = true
        };

        static bool IsLoopback( HttpContext c )
        {
            var host = c.Request.Host.Host;
            return string.Equals( host, "localhost", StringComparison.OrdinalIgnoreCase )
                   || (IPAddress.TryParse( host.Trim( '[', ']' ), out var ip ) && IPAddress.IsLoopback( ip ));
        }
    }
}
