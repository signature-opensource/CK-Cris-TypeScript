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

The route is declared once, on the package ([`CrisAspNetAuthenticationPackage`](CrisAspNetAuthenticationPackage.cs)):

```csharp
[CrisRoute<ICommandAuthenticationSession>( AuthRoute )]
public class CrisAspNetAuthenticationPackage : TypeScriptPackage
```

This single declaration is used by both sides: the generated TypeScript client posts the session commands to
`/.cris/auth`, and `CrisAspNetService` accepts a command only on its route (and a route accepts only its commands). A
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

## The TypeScript client: `AuthService`

The package ships a framework-free [`AuthService`](Res/AuthService.ts) on top of the generated `HttpCrisEndpoint`:

```ts
const ep = new HttpCrisEndpoint( axios );
const auth = await AuthService.createAsync( ep );
// A session command is sent like any other command: the AuthService follows it.
await ep.sendAsync( new BasicLoginCommand( "Alice", "secret", true ) );
auth.actor.actorName; // "Alice"
```

- **The endpoint updates the state.** The `AuthService` registers a `HttpCrisEndpointHook`: it adds the bearer token to
  every command (`/.cris/auth` included: the impersonation reads the current authentication), and it applies the result
  of every session command, whoever sends it. There is no `login( ... )` method: the application sends the login
  command of its choice.
- **What changes the state:** a successful `SessionResult` (a new token); a logout; a failed refresh (the server has
  cleared the session). A failed login or impersonation changes nothing.
- **The token is cached in `localStorage`** and shared by the tabs (the `storage` event). Every session command rotates
  the session secrets (the cookies), so a tab with an old token would be anonymous. The refreshes are serialized across
  the tabs (`navigator.locks`): a tab that waited adopts the token of the other one. At load, the cached token is used
  when it has not expired, otherwise a refresh is sent (the refresh cookie may be valid).
- **The levels are derived:** `level`/`actualLevel` apply the expirations; `actor`/`actualActor` are the anonymous below
  `Normal` (`authenticationInfo.unsafeActor` is the explicit access to an unsafe identity).
- **Helpers:** `refresh()`, `logout()`, `impersonate( actor )`, `clearImpersonation()`, `addOnChange` /
  `removeOnChange`, `close()`. After every change, the ambient values of the endpoint are updated.

## Not yet

- The remote logins (OIDC): `IStartLoginCommand`, the challenge and the callback.
- A tenant domain per host: the current local party is the root one.
