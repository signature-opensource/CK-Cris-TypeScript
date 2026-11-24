The engine of CK.Cris.AspNet and CK.Cris.TypeScript: generates the routes of the commands and the TypeScript of the
Cris commands and endpoints.

`CrisAspNetServiceImpl` generates the route of each command (from the `[CrisRoute<T>]` declarations, the same for the
client and the server). `TypeScriptCrisCommandGeneratorImpl` generates the TypeScript side: `CK/Cris/Model.ts`, the
`CommandModel` of each command, the `CrisEndpoint` and the `HttpCrisEndpoint`.
