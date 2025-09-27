using CK.TypeScript;
using CK.TS.Angular;
using CK.Core;
using CK.Ng.Axios;

namespace CK.Ng.Cris.AspNet;

[TypeScriptPackage]
[Requires<AxiosPackage>]
[NgProviderImport( "AXIOS, HttpCrisEndpoint" )]
[NgProviderImport( "inject", From = "@angular/core" )]
[NgProvider( "{ provide: HttpCrisEndpoint, useFactory: () => new HttpCrisEndpoint( inject( AXIOS ) ) }" )]
public class CrisAspNetPackage : TypeScriptPackage
{
}
