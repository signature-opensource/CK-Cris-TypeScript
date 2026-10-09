# CK.Ng.Cris.AspNet.Authentication

The Angular front of the new authentication ([CK.Cris.AspNet.Authentication](../CK.Cris.AspNet.Authentication/README.md)).
It coexists with the old CK.Ng.AspNet.Auth, but an application uses one of them: their private page and user info box
are single components (`INgPrivatePageComponent`, `INgUserInfoBoxComponent`), so referencing both fails at setup.

## What it provides

- The `AuthService` on the `HttpCrisEndpoint`. The application starts once it is initialized (its cached token is used
  or the session is refreshed).
- `NgAuthService`: the authentication state as signals (`authenticationInfo`, `actor`, `actualActor`, `level`,
  `isImpersonated`).
- **No HttpClient interceptor.** The authentication applies to the Cris commands only: the bearer token is accepted
  with the binding cookie, which is sent only to `/.cris`.
- The components:

| Component | Route | What |
|---|---|---|
| `AuthenticationPage` | `/auth` | The authentication layout (logo, back button). |
| `Login` | `/auth` | The welcome text, the login forms and a button per remote scheme (`Oidc.<Suffix>`, the last used first, a logo from `login-providers/<suffix>.png`; in a popup by default, `remoteLoginInline` for the current window). It redirects once the actor is not the anonymous. |
| `Logout` | `/auth/logout` | Sends the logout. |
| `PrivatePage` | `/` | Its routes are guarded: the actor must not be the anonymous (an Unsafe identity is the anonymous). Otherwise, redirection to `/auth?redirectTo=...`. |
| `UserInfoBox` | | The actor, the actual actor when impersonating, the level and the sign out. |

The remote login errors are notified (a closed popup is not an error).

## CK.Ng.Cris.AspNet.Authentication.Basic

The Basic login form (user name, password, remember me), injected in the `Login` component. It sends the
`IBasicLoginCommand` through the `HttpCrisEndpoint`: the `AuthService` follows it like any session command.

## CK.Ng.Cris.AspNet.Authentication.Weak

The Weak login form (a weak actor name, no password), injected in the `Login` component after the remote schemes. It
sends the `IWeakLoginCommand` through the `HttpCrisEndpoint`. A weak actor is at the Unsafe level: it is **not
authenticated** (`actor` stays the anonymous, `authenticationInfo.unsafeActor` is the weak actor), so the `PrivatePage`
refuses it and the `Login` component does not redirect it. On success, the form navigates to its `redirectTo` input
(`/` by default): with CK.Ng.PublicPage, `/` is the public page when the private one refuses the actor. The application
routes there what a weak actor may do (its commands are the `ICommandAuthUnsafe` ones).
