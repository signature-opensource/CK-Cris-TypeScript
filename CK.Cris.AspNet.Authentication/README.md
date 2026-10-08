# CK.Cris.AspNet.Authentication

The HTTP front of the CK.AuthenticationInfo model (it replaces CK.AspNet.Auth / WebFrontAuth). The deployment target is
**same-origin**: the SPA and `/.cris` share the same origin, there is no CORS.

```csharp
builder.Services.AddCrisAuthentication();
// ...
app.UseCrisAuthentication();   // Before UseCris().
app.UseCris();
```

## Routes

| Route | What |
|---|---|
| `/.cris/auth` | The session commands (`ICommandAuthenticationSession`: logins, refresh, logout, impersonation) and only them. POST with a JSON content only. |
| `/.cris` | The other commands. A session command is refused here. |

The [`SessionCommandRouteGuard`](SessionCommandRouteGuard.cs) (a `CK.Cris.AspNet.ICrisRouteGuard`) enforces this. A
session command is relayed to a remote authentication center when there is one (`IAuthenticationSessionRelay`), before
the handlers (the `executeOverride` of `CrisAspNetService.HandleRequestAsync`), and executed here otherwise.

## Same origin, always checked

Every request to `/.cris*` must come from the same origin ([`CrisAuthenticationMiddleware`](CrisAuthenticationMiddleware.cs)):
`Sec-Fetch-Site: same-origin`, or an `Origin` equal to the request's origin. Otherwise: 403. A request with neither header
is not sent by a browser (no cross-site risk) and is accepted. This check is mandatory: the cookies are `SameSite=Strict`,
which a sibling sub-domain bypasses.

## The cookies ([`HttpSessionEndpoint`](HttpSessionEndpoint.cs))

| Cookie | Path | Holds |
|---|---|---|
| `ck-auth-binding` | `/.cris` | The binding secret: every authenticated request presents it. |
| `ck-auth-refresh` | `/.cris/auth` | The session key and the refresh secret: only the session commands see it. |

`HttpOnly`, `SameSite=Strict`, host-only, `Secure` (except over http on a loopback address, for the development);
persistent when the session is remembered. The secrets are generated here: the authentication center only knows their
hashes (base64url SHA-256). When a session command is relayed, the next secrets are prepared before the relay and their
hashes travel with the command.

## The bearer token

A request that presents a bearer token is authenticated with `IAuthenticationTokenReader` (implemented by the
authentication center package) **only if it also presents the binding secret of the token's session**: a stolen token
cannot be used from another browser. Otherwise the request is anonymous. The authentication of the request is the scoped
`AuthenticationInfo` (see [`HttpAuthenticationContext`](HttpAuthenticationContext.cs)).

## Not yet

- The remote logins (OIDC): `IStartLoginCommand`, the challenge and the callback.
- A tenant domain per host: the current local party is the root one.
