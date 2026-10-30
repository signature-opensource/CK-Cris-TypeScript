# CK.Cris.AspNet.Authentication

The HTTP side of the CK.AuthenticationInfo model, for the front applications. The deployment target is
**same-origin**: the SPA and `/.cris` share the same origin, there is no CORS.

```csharp
builder.Services.AddCrisAuthentication();
// ...
app.UseCrisAuthentication();   // Before UseCris().
app.UseCris();
```

## One authentication stack per application

The packages of the former stack (WebFrontAuth: CK.AspNet.Auth and the packages built on it) are deprecated: their
last versions are tombstones that fail the build. An application that still references a former version of one of
them is refused:

| Combination | Refused by |
|---|---|
| CK.Cris.Auth + CK.Cris.Authentication | the setup: both define `CK.Auth.CrisAuthenticationService` (the generated code doesn't compile). |
| CK.AspNet.Auth (WebFrontAuth) + this package | `UseCrisAuthentication()` throws at startup (the `IStObjMap` features contain `CK.AspNet.Auth`). |
| CK.Ng.AspNet.Auth + CK.Ng.Cris.AspNet.Authentication | the setup: the private page and the user info box are single components. |

## Routes

| Route | What |
|---|---|
| `/.cris/auth` | The session commands (`ICommandAuthenticationSession`: logins, refresh, logout, impersonation) and only them. POST with a JSON content only. |
| `/.cris` | The other commands, wherever they are handled (the authentication center's ones included: where a command executes is a deployment detail). A session command is refused here. |
| `/.auth/oidc/callback` | The callback of the remote logins (see below): a GET, outside `/.cris` (it is a cross-site navigation started by the provider). |

The routes are declared once, on the package ([`CrisAspNetAuthenticationPackage`](CrisAspNetAuthenticationPackage.cs)):

```csharp
[CrisRoute<ICommandAuthenticationSession>( AuthRoute )]
public class CrisAspNetAuthenticationPackage : TypeScriptPackage
```

This declaration is used by both sides: the generated TypeScript client posts each command to its route, and
`CrisAspNetService` accepts a command only on its route (and a route accepts only its commands).

A session command is always executed here, by this endpoint (CK.Cris.Authentication): what it needs from the
authentication center is an operation that the endpoint executes, locally in the authentication center application,
dispatched to it from a remote of it (CK.AppIdentity.Cris). The other commands handled by the authentication center are
dispatched the same way, with the user's proof (see CK.AppIdentity.Cris.Authentication).

Only the public commands are received: an `[Internal]` or `[Private]` command is refused on every route (see
`CrisAspNetService.HandleRequestAsync`).

**The caller party.** A request is received directly: the `CallerPartyId` of its `AuthenticationInfo` is the
`PartyActorId` of the `CurrentLocalParty` (this package references CK.AppIdentity.Abstractions). It is 0 when the party
model is not installed (no `IApplicationIdentityService`) or when the identifier is not resolved yet.

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
hashes (base64url SHA-256). When a session command needs the authentication center, the next secrets are prepared
before its operation is executed and only their hashes travel with it.

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
  or an `ExecutionError`, including a remote that cannot reach its authentication center), the session is kept and the
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
endpoint:

1. **Start:** an `IStartLoginCommand` on `/.cris/auth`. The endpoint gives its callback URL (`/.auth/oidc/callback` of the
   request's origin: the authentication center accepts only the configured ones) and sets the `ck-auth-login` cookie
   (the hash of its correlation secret goes with the pending login). The result is the provider's URL.
2. **Callback:** the provider redirects the browser to `/.auth/oidc/callback?state=...&code=...`. The endpoint builds the
   `IRemoteLoginCallbackCommand` (a client never sends it: it is refused on `/.cris/auth`), executes it, and
   deletes the `ck-auth-login` cookie. The session cookies are set here; the response never carries a token.
3. **End:**
   - **popup:** a page that posts `{ type: "ck-auth-remote-login", success, bound, error }` to its own origin and closes (CSP with
     a nonce, `no-store`, `no-referrer`);
   - **inline:** a `303` to the return URL (relative only: anything else becomes `/`) with the `ck-auth-login` marker
     (`ok`, `bound` or the resource name of the error).

On the client, `AuthService.startPopupLogin( scheme )` (from a click: the popup opens before anything else, browsers
block a later one; only a message of this origin from this popup ends the login) and `startInlineLogin( scheme,
returnUrl )`; then the session is refreshed (the marker forces it at the load of the return URL: the cached token is
stale). `refreshSchemesAsync()` reads the schemes (`availableSchemes`: their `name`, the remote ones are
`Oidc.<Suffix>`, and their `criticalLifetime`, zero when the scheme cannot elevate an actor to the Critical level) and
`remoteLoginError` is the error of the last remote login. `criticalSchemes` are the schemes of the actual actor that
can elevate it: the ones to propose when a Critical level is required (empty when the user cannot reach it).

Both start methods take an optional `critical`: the login is meant to reach the Critical level, the remote provider must
authenticate the user again (`IStartLoginCommand.Critical`).

**Binding an identity** (`IStartLoginCommand.BindToCurrentActor`): `startPopupBinding( scheme )` and
`startInlineBinding( scheme, returnUrl )` add the remote identity to the account of the current actor (Critical level,
not impersonated). The callback recognizes a binding by its successful result without a token: it sets no cookie, the
popup message has `bound: true`, the inline marker is `bound`. The client then keeps its session: no refresh (a refresh
would lose the Critical level). `remoteLoginBound` is true after a successful binding.

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
