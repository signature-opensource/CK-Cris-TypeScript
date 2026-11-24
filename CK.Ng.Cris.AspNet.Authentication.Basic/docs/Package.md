The Basic login form (user name and password) of CK.Ng.Cris.AspNet.Authentication: it sends the `IBasicLoginCommand`.

The `NgCrisAuthenticationBasicPackage` injects the form in the login page and brings the command handlers of the scheme
(CK.Cris.Authentication.Basic) to the endpoint that serves it.
