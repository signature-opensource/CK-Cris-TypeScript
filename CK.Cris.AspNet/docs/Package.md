The HTTP endpoint of Cris (Command Responsibility Segregation): the "/.cris" middleware receives the commands of the
client applications, validates and executes them, and returns their results.

`CrisAspNetService.HandleRequestAsync` reads, validates and executes a command; the `CrisMiddleware` serves "/.cris".
`CrisAspNetHosting` contributes it to the web applications of a host (like the "HttpEndpoint" feature of
CK.AppIdentity.HttpEndpoint); `CrisApplicationBuilderExtension.EnableCris` does it on a `WebApplicationBuilder`.
A package declares the route of some commands with `[CrisRoute<T>]` (under "/.cris"): the client posts them there and
the server accepts them only there.
