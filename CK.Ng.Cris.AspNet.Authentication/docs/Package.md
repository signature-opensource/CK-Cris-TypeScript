The Angular side of CK.Cris.AspNet.Authentication: the `AuthService` provider, the `NgAuthService` signals, the private
page guard and the authentication components.

The `NgCrisAuthenticationPackage` provides the `AuthService` on the `HttpCrisEndpoint` and waits for its initialization at
startup. `NgAuthService` exposes the authentication state as signals. The components are the authentication page, the
login and logout components, the private page and the user info box. The authentication applies to the Cris commands
only: there is no HttpClient interceptor.

See the [README](https://github.com/signature-opensource/CK-Cris-TypeScript/blob/stable/CK.Ng.Cris.AspNet.Authentication/README.md).
