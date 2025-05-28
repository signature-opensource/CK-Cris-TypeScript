using CK.TypeScript;
using CK.TS.Angular;

namespace CK.Ng.Cris.AspNet;

[TypeScriptPackage]
[NgProviderImport( "AXIOS, HttpCrisEndpoint" )]
[NgProviderImport( "inject", LibraryName = "@angular/core" )]
[NgProvider( "{ provide: HttpCrisEndpoint, useFactory: () => new HttpCrisEndpoint( inject( AXIOS ) ) }" )]
public class TSPackage : TypeScriptPackage
{
    void StObjConstruct( CK.Ng.Axios.TSPackage axios ) { }
}
