The passkey (WebAuthn) buttons of CK.Ng.Cris.AspNet.Authentication: "Sign in with a passkey" in the login page, and
"Add a passkey" for the current user.

`ck-passkey-login-button` is injected in the login page; it shows only when the "Passkey" scheme is available and the
browser supports passkeys. `ck-passkey-registration-button` is placed by the application where the user manages their
account. Both use the `PasskeyService` of CK.Cris.AspNet.Authentication.Passkey.

See the [README](https://github.com/signature-opensource/CK-Cris-TypeScript/blob/stable/CK.Ng.Cris.AspNet.Authentication.Passkey/README.md).
