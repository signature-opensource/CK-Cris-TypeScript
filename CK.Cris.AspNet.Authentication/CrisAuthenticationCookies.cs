namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The session cookies of the HTTP front.
/// <list type="bullet">
///   <item>
///   The <b>binding</b> cookie (<see cref="BindingCookieName"/>, <see cref="BindingCookiePath"/>) holds the binding secret:
///   every authenticated request presents it, and a bearer token is accepted only with the binding secret of its session.
///   </item>
///   <item>
///   The <b>refresh</b> cookie (<see cref="RefreshCookieName"/>, <see cref="RefreshCookiePath"/>) holds the session key and
///   the refresh secret: only the session commands see it.
///   </item>
/// </list>
/// Both are <c>HttpOnly</c>, <c>SameSite=Strict</c>, host-only and <c>Secure</c> (except over http on a loopback address,
/// for the development). They are persistent when the session is remembered, session cookies otherwise.
/// </summary>
public static class CrisAuthenticationCookies
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
    /// The path of the refresh cookie: the route of the session commands.
    /// </summary>
    public const string RefreshCookiePath = CrisAspNetAuthenticationPackage.AuthRoute;

    /// <summary>
    /// The name of the remote login cookie: the correlation secret of a remote login in progress (and how it ends: popup
    /// or inline, the return URL). It is <c>SameSite=Lax</c> (the provider's redirection back is a cross-site navigation:
    /// a <c>Strict</c> cookie would not be sent), <c>HttpOnly</c>, scoped to the <see cref="RemoteLoginCallbackPath"/>,
    /// 10 minutes long and deleted by the callback.
    /// </summary>
    public const string RemoteLoginCookieName = "ck-auth-login";

    /// <summary>
    /// The path of the callback of the remote logins: "/.auth/oidc/callback". It is outside "/.cris": it is a cross-site
    /// GET started by the provider, that the same-origin check of "/.cris" refuses.
    /// </summary>
    public const string RemoteLoginCallbackPath = "/.auth/oidc/callback";
}
