The HTTP side of the CK.AuthenticationInfo model: the "/.cris/auth" route of the session commands, the session cookies
and the bearer tokens of "/.cris".

The `CrisAuthenticationMiddleware` authenticates the "/.cris" requests (same origin only, a bearer token accepted with
the binding cookie of its session) and serves the session commands; `CrisAuthenticationCookies` describes the cookies.
`CrisAspNetAuthenticationHosting` contributes it to the web applications of a host;
`CrisAuthenticationExtensions.EnableCrisAuthentication` does it on a `WebApplicationBuilder`. Its TypeScript side is
the framework-free `AuthService`.

See the [README](https://github.com/signature-opensource/CK-Cris-TypeScript/blob/stable/CK.Cris.AspNet.Authentication/README.md).
