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
/// The session side of the HTTP front (<see cref="IAuthenticationSessionEndpoint"/>): the session secrets are in two cookies
/// (see <see cref="CrisAuthenticationCookies"/>).
/// <para>
/// The secrets are generated here and never leave this endpoint: the authentication center only knows their hashes
/// (base64url SHA-256 of the secret).
/// </para>
/// <para>
/// This is internal on purpose: an implementation of a <see cref="ScopedContainerConfiguredServiceAttribute"/> interface is
/// provided by its endpoint, never built by the DI. The endpoint of a request is created by the <see cref="CrisAuthenticationMiddleware"/>
/// and provided by the <see cref="HttpAuthenticationContext"/> (see <see cref="Microsoft.Extensions.DependencyInjection.CrisAuthenticationExtensions.AddCrisAuthentication"/>).
/// A public class would be registered by the type discovery as soon as this assembly is registered.
/// </para>
/// </summary>
sealed class HttpSessionEndpoint : IAuthenticationSessionEndpoint
{
    const string BindingCookieName = CrisAuthenticationCookies.BindingCookieName;
    const string RefreshCookieName = CrisAuthenticationCookies.RefreshCookieName;
    const string BindingCookiePath = CrisAuthenticationCookies.BindingCookiePath;
    const string RefreshCookiePath = CrisAuthenticationCookies.RefreshCookiePath;

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

    /// <summary>
    /// The lifetime of the remote login cookie: 10 minutes (the one of a pending login).
    /// </summary>
    public static readonly TimeSpan RemoteLoginLifetime = TimeSpan.FromMinutes( 10 );

    (bool Inline, string? ReturnUrl)? _remoteLoginEnd;

    /// <summary>
    /// Sets how the remote login that starts ends (see <see cref="PrepareRemoteLogin"/>): only the front needs it.
    /// </summary>
    /// <param name="inline">True for an inline login (redirection to the return URL), false for a popup.</param>
    /// <param name="returnUrl">The relative return URL of an inline login.</param>
    internal void SetRemoteLoginEnd( bool inline, string? returnUrl ) => _remoteLoginEnd = (inline, returnUrl);

    /// <inheritdoc />
    public string? RemoteLoginCallbackUrl
    {
        get
        {
            var r = _context.Request;
            return $"{r.Scheme}://{r.Host}{r.PathBase}{CrisAuthenticationCookies.RemoteLoginCallbackPath}";
        }
    }

    /// <inheritdoc />
    public string PrepareRemoteLogin()
    {
        var secret = NewSecret();
        var (inline, returnUrl) = _remoteLoginEnd ?? (false, null);
        var value = secret + '.' + (inline ? 'i' : 'p') + '.' + Base64Url.EncodeToString( Encoding.UTF8.GetBytes( returnUrl ?? "" ) );
        _context.Response.Cookies.Append( CrisAuthenticationCookies.RemoteLoginCookieName,
                                          value,
                                          CreateOptions( CrisAuthenticationCookies.RemoteLoginCallbackPath, DateTimeOffset.UtcNow + RemoteLoginLifetime, SameSiteMode.Lax ) );
        return Hash( secret );
    }

    /// <summary>
    /// Gets the remote login presented by the caller (the remote login cookie): its correlation secret and how it ends.
    /// The return URL comes from the caller's cookie: it must be checked again before being used.
    /// </summary>
    internal (string Secret, bool Inline, string? ReturnUrl)? PresentedRemoteLogin
    {
        get
        {
            var v = _context.Request.Cookies[CrisAuthenticationCookies.RemoteLoginCookieName];
            if( v == null ) return null;
            var parts = v.Split( '.' );
            if( parts.Length != 3 || parts[0].Length == 0 || (parts[1] != "i" && parts[1] != "p") ) return null;
            string? returnUrl;
            try
            {
                returnUrl = Encoding.UTF8.GetString( Base64Url.DecodeFromChars( parts[2] ) );
            }
            catch( FormatException )
            {
                return null;
            }
            return (parts[0], parts[1] == "i", returnUrl.Length > 0 ? returnUrl : null);
        }
    }

    /// <inheritdoc />
    public string? PresentedRemoteLoginHash => PresentedRemoteLogin is { } r ? Hash( r.Secret ) : null;

    /// <summary>
    /// Deletes the remote login cookie (a remote login can be completed once).
    /// </summary>
    internal void ClearRemoteLogin()
    {
        _context.Response.Cookies.Delete( CrisAuthenticationCookies.RemoteLoginCookieName,
                                          CreateOptions( CrisAuthenticationCookies.RemoteLoginCallbackPath, null, SameSiteMode.Lax ) );
    }

    CookieOptions CreateOptions( string path, DateTimeOffset? expires, SameSiteMode sameSite = SameSiteMode.Strict )
    {
        return new CookieOptions
        {
            Path = path,
            HttpOnly = true,
            SameSite = sameSite,
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
