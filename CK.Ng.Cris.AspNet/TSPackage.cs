using CK.TypeScript;
using CK.TS.Angular;
using CK.Core;

namespace CK.Ng.Cris.AspNet;

[TypeScriptPackage]
[Requires<CK.Ng.Axios.TSPackage>]
[NgProviderImport( "AXIOS, HttpCrisEndpoint" )]
[NgProviderImport( "inject", From = "@angular/core" )]
[NgProvider( "{ provide: HttpCrisEndpoint, useFactory: () => new HttpCrisEndpoint( inject( AXIOS ) ) }" )]
public class TSPackage : TypeScriptPackage
{
}
