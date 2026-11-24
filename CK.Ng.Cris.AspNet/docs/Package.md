Cris in an Angular application: provides the `HttpCrisEndpoint` that sends the commands to the "/.cris" endpoint of the
server.

The `CrisAspNetPackage` provides the `HttpCrisEndpoint` on the `AXIOS` instance of CK.Ng.Axios, and the culture of the
commands follows the locale of the application (`LocaleService`).
