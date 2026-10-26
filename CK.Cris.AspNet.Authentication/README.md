# CK.Cris.AspNet.Authentication

The HTTP front of the CK.AuthenticationInfo model (it replaces CK.AspNet.Auth / WebFrontAuth). The deployment target is
**same-origin**: the SPA and `/.cris` share the same origin, there is no CORS.

```csharp
builder.Services.AddCrisAuthentication();
// ...
app.UseCrisAuthentication();   // Before UseCris().
app.UseCris();
```

## One authentication stack per application

This package coexists with the old stack (WebFrontAuth) in the repositories, but an application uses one of them:

| Combination | Refused by |
|---|---|
| CK.Cris.Auth (old) + CK.Cris.Authentication (new) | the setup: both define `CK.Auth.CrisAuthenticationService` (the generated code doesn't compile). |
| CK.AspNet.Auth (WebFrontAuth) + this package | `UseCrisAuthentication()` throws at startup (the `IStObjMap` features contain `CK.AspNet.Auth`). |
| CK.Ng.AspNet.Auth + CK.Ng.Cris.AspNet.Authentication | the setup: the private page and the user info box are single components. |

The other old packages depend on one of these.

## Routes

| Route | What |
|---|---|
| `/.cris/auth` | The session commands (`ICommandAuthenticationSession`: logins, refresh, logout, impersonation) and only them. POST with a JSON content only. |
| `/.cris/auth-center` | The commands executed by the authentication center (`ICommandAuthCenter`: the actor's self-service on its own login methods, the login schemes) and only them. POST with a JSON content only. The refresh cookie is not sent here. |
| `/.cris` | The other commands. A session command or an authentication center command is refused here. |
| `/.auth/oidc/callback` | The callback of the remote logins (see below): a GET, outside `/.cris` (it is a cross-site navigation started by the provider). |

The routes are declared once, on the package ([`CrisAspNetAuthenticationPackage`](CrisAspNetAuthenticationPackage.cs)):

```csharp
[CrisRoute<ICommandAuthenticationSession>( AuthRoute )]
[CrisRoute<ICommandAuthCenter>( AuthCenterRoute )]
public class CrisAspNetAuthenticationPackage : TypeScriptPackage
```

These declarations are used by both sides: the generated TypeScript client posts each command to its route, and
`CrisAspNetService` accepts a command only on its route (and a route accepts only its commands). The execution is
overridden on both routes (the `executeOverride` of `CrisAspNetService.HandleRequestAsync`):
- a session command is relayed to a remote authentication center when there is one (`IAuthenticationSessionRelay`);
- an authentication center command is relayed, after its incoming validation here, with the caller's proof: the bearer
  token and the hash of the binding secret (`IAuthCenterCommandRelay`, `IAuthenticationSessionEndpoint.PresentedBindingSecretHash`).
  The remote authentication center establishes the identity again.

Both are executed here otherwise (no authentication center, or a local one).

## Same origin, always checked

Every request to `/.cris*` must come from the same origin ([`CrisAuthenticationMiddleware`](CrisAuthenticationMiddleware.cs)):
`Sec-Fetch-Site: same-origin`, or an `Origin` equal to the request's origin. Otherwise: 403. A request with neither header
is not sent by a browser (no cross-site risk) and is accepted. This check is mandatory: the cookies are `SameSite=Strict`,
which a sibling sub-domain bypasses.

## The cookies ([`CrisAuthenticationCookies`](CrisAuthenticationCookies.cs))

| Cookie | Path | Holds |
|---|---|---|
| `ck-auth-binding` | `/.cris` | The binding secret: every authenticated request presents it. |
| `ck-auth-refresh` | `/.cris/auth` | The session key and the refresh secret: only the session commands see it. |
| `ck-auth-login` | `/.auth/oidc/callback` | A remote login in progress: its correlation secret, popup or inline, the return URL. **`SameSite=Lax`** (the provider's redirection back is a cross-site navigation), 10 minutes, deleted by the callback. |

`HttpOnly`, `SameSite=Strict` (except `ck-auth-login`), host-only, `Secure` (except over http on a loopback address, for the development);
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
- **What changes the state:** a successful `SessionResult` (a new token); a logout; a refresh **refused by the server**
  (a failed `SessionResult` or a validation error: the server has decided, the session ends). A failed login or
  impersonation changes nothing.
- **A refresh that decided nothing is retried.** When the server could not be reached or failed (a `CommunicationError`
  or an `ExecutionError`, including a front that cannot reach its authentication center), the session is kept and the
  refresh is retried with a backoff: 1 s, 2 s, 4 s... at most 30 s (`retryMinDelay`, `retryMaxDelay`), endlessly. The
  server answers a decision when the session has really ended. Coming back online or to the tab (`online`,
  `visibilitychange`) retries at once. If the refresh was executed but its answer lost, the authentication center
  accepts the previous secrets once (its `RefreshGraceWindow`): the session survives.
- **The last sent session command wins:** the answer of an older one that arrives late (a refresh sent before a logout,
  for instance) is ignored.
- **The token is cached in `localStorage`** and shared by the tabs (the `storage` event). Every session command rotates
  the session secrets (the cookies), so a tab with an old token would be anonymous. The refreshes are serialized across
  the tabs (`navigator.locks`): a tab that waited adopts the token of the other one. At load, the cached token is used
  when it has not expired, otherwise a refresh is sent (the refresh cookie may be valid).
- **The levels are derived:** `level`/`actualLevel` apply the expirations; `actor`/`actualActor` are the anonymous below
  `Normal` (`authenticationInfo.unsafeActor` is the explicit access to an unsafe identity).
- **Helpers:** `refresh()`, `logout()`, `impersonate( actor )`, `clearImpersonation()`, `addOnChange` /
  `removeOnChange`, `close()`. After every change, the ambient values of the endpoint are updated.

## The remote logins (OpenID Connect)

The authentication center holds the client secrets and does the protocol (see CK.AppIdentity.Cris.AuthCenter). This
front:

1. **Start:** an `IStartLoginCommand` on `/.cris/auth`. The endpoint gives its callback URL (`/.auth/oidc/callback` of the
   request's origin: the authentication center accepts only the configured ones) and sets the `ck-auth-login` cookie
   (the hash of its correlation secret goes with the pending login). The result is the provider's URL.
2. **Callback:** the provider redirects the browser to `/.auth/oidc/callback?state=...&code=...`. The front builds the
   `IRemoteLoginCallbackCommand` (a client never sends it: it is refused on `/.cris/auth`), relays or executes it, and
   deletes the `ck-auth-login` cookie. The session cookies are set here; the response never carries a token.
3. **End:**
   - **popup:** a page that posts `{ type: "ck-auth-remote-login", success, error }` to its own origin and closes (CSP with
     a nonce, `no-store`, `no-referrer`);
   - **inline:** a `303` to the return URL (relative only: anything else becomes `/`) with the `ck-auth-login` marker
     (`ok` or the resource name of the error).

On the client, `AuthService.startPopupLogin( scheme )` (from a click: the popup opens before anything else, browsers
block a later one; only a message of this origin from this popup ends the login) and `startInlineLogin( scheme,
returnUrl )`; then the session is refreshed (the marker forces it at the load of the return URL: the cached token is
stale). `refreshSchemesAsync()` reads the schemes (`availableSchemes`, the remote ones are `Oidc.<Suffix>`) and
`remoteLoginError` is the error of the last remote login.

Both start methods take an optional `userData` (a `Record` or a `Map`, see `LoginUserData` and `setLoginUserData`): free
data of the login, an invitation token for instance. The authentication center keeps them until the callback (the browser
never carries them back); they are untrusted and limited (16 entries, 4096 characters).

## The passkeys

The endpoint's `WebOrigin` is the origin that the session route requires from the browser: the passkey ceremonies are
accepted by the authentication center only from configured origins, and the origin signed by the browser must be this
one. Behind a reverse proxy, the request's scheme and host must be the public ones (forwarded headers), as for the
same-origin check. The TypeScript side is in [CK.Cris.AspNet.Authentication.Passkey](../CK.Cris.AspNet.Authentication.Passkey/README.md):
the `AuthService` has no passkey member.

## Not yet

- A tenant domain per host: the current local party is the root one.
