Maps Cris commands to Web API routes: a POST, PATCH or PUT request on a route reads its command and executes it like
the "/.cris" endpoint.

`CrisWebApiExtensions.MapApiPost<T>`, `MapApiPatch<T>` and `MapApiPut<T>` map a route pattern to a command of type `T`
(optionally read by a `CommandRequestReader`) handled by `CrisAspNetService.HandleRequestAsync`; the `CrisWebResult`
writes the result.
