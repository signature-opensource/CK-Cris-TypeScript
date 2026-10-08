using CK.Core;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The authentication of a request to "/.cris": set by the <see cref="CrisAuthenticationMiddleware"/>, it provides the
/// scoped <see cref="AuthenticationInfo"/> and <see cref="CK.Auth.IAuthenticationSessionEndpoint"/> of the request
/// (see <see cref="CrisAuthenticationExtensions.AddCrisAuthentication"/>).
/// </summary>
public sealed class HttpAuthenticationContext
{
    /// <summary>
    /// Gets the authentication of the request: <see cref="AuthenticationInfo.None"/> until the middleware authenticates it.
    /// </summary>
    public AuthenticationInfo Info { get; internal set; } = AuthenticationInfo.None;

    /// <summary>
    /// Gets the session endpoint of the request. Null outside of "/.cris".
    /// </summary>
    public HttpSessionEndpoint? Endpoint { get; internal set; }
}
