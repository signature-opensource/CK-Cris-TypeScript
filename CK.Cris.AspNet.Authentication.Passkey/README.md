# CK.Cris.AspNet.Authentication.Passkey

The passkey (WebAuthn) logins of the HTTP front, on the TypeScript side: the framework-free
[`PasskeyService`](Res/PasskeyService.ts), over the generated `HttpCrisEndpoint`. The authentication center runs the
protocol and verifies (CK.AppIdentity.Cris.AuthCenter.Passkey); this package runs the browser's part. The
[`AuthService`](../CK.Cris.AspNet.Authentication/README.md) has no passkey member: this package exists only when passkeys
are wanted.

```ts
const passkeys = new PasskeyService( endpoint );
if( PasskeyService.isSupported ) {
  const outcome = await passkeys.loginAsync( rememberMe );   // From a click.
  if( outcome.cancelled ) { /* The user cancelled: nothing to display. */ }
  else if( outcome.error ) { /* outcome.error: a message or a resource name. */ }
  // On success, the AuthService follows the session (the login is a session command).
}
```

- **A ceremony is two commands** with the browser in between: the start command returns the WebAuthn options as JSON
  (`PublicKeyCredential.parseRequestOptionsFromJSON` / `parseCreationOptionsFromJSON`), `navigator.credentials.get` or
  `create` asks the authenticator (the user verifies with a PIN or biometrics), and the finish command sends
  `credential.toJSON()`. This is why there is a helper here, unlike the direct logins.
- **`loginAsync( rememberMe?, userData? )`:** a usernameless login (the browser proposes the passkeys it holds for the
  site). The finish command is a session command sent through the endpoint: the `AuthService` follows it.
- **`registerAsync( name? )`:** adds a passkey to the current actor. It requires the Critical level (a recent login) and is
  refused while impersonating; the commands' `ActorId` is the endpoint's ambient value.
- **`isSupported`:** `PublicKeyCredential` with the WebAuthn Level 3 JSON helpers, and `navigator.credentials`. No polyfill.
- **The outcome:** `cancelled` when the user cancelled or the authenticator timed out (`NotAllowedError`, `AbortError`:
  WebAuthn doesn't tell them apart, on purpose): not an error. `error` is the server's message, or a resource name:
  `Authentication.PasskeyNotSupported`, `Authentication.PasskeyExists` (the authenticator already holds a passkey of the
  actor), `Authentication.PasskeyLoginFailed`, `Authentication.PasskeyRegistrationFailed`.

The Angular buttons are in [CK.Ng.Cris.AspNet.Authentication.Passkey](../CK.Ng.Cris.AspNet.Authentication.Passkey/README.md).
