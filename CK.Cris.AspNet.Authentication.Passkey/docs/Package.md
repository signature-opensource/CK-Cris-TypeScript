The passkey (WebAuthn) logins of the client applications, on the TypeScript side: the framework-free `PasskeyService`
(login, registration) over the Cris endpoint.

The `PasskeyService` sends the passkey commands through the `HttpCrisEndpoint` and runs the browser's part of the
ceremonies; the authentication center verifies. The package also brings the command handlers of the passkey session
commands (CK.Cris.Authentication.Passkey).

See the [README](https://github.com/signature-opensource/CK-Cris-TypeScript/blob/stable/CK.Cris.AspNet.Authentication.Passkey/README.md).
