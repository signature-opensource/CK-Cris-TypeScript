The Weak login form (a weak actor name, no password) of CK.Ng.Cris.AspNet.Authentication: it sends the
`IWeakLoginCommand`.

The `NgCrisAuthenticationWeakPackage` injects the form in the login page; a weak actor logs in with its name only, at
the Unsafe level. It brings the command handlers of the scheme (CK.Cris.Authentication.Weak) to the endpoint that
serves it.
