# CK.Ng.Cris.AspNet.Authentication.Passkey

The passkey (WebAuthn) buttons of [CK.Ng.Cris.AspNet.Authentication](../CK.Ng.Cris.AspNet.Authentication/README.md), on top
of the [`PasskeyService`](../CK.Cris.AspNet.Authentication.Passkey/README.md).

- **`ck-passkey-login-button`** ("Sign in with a passkey") is injected in the login page (`<LoginProviders>`). It shows
  only when the authentication center offers the "Passkey" scheme and the browser supports passkeys. On success, the
  login page follows the session and redirects; a cancellation displays nothing.
- **`ck-passkey-registration-button`** ("Add a passkey") is placed by the application where the user manages their
  account. It is enabled only at the Critical level (a recent login) and when not impersonated; inputs: `passkeyName`
  (optional), output: `registered` (the name of the new passkey).

Listing, renaming and deleting the passkeys of the user belong to the storage package (its commands), not here.
