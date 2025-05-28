using CK.TS.Angular;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Auth;

[TypeScriptPackage]
[NgProviderImport( "provideNgCrisAspNetAuthSupport" )]
[NgProvider( "provideNgCrisAspNetAuthSupport()", "#Support" )]
public class TSPackage : TypeScriptPackage
{
    void StObjConstruct( CK.AspNet.Auth.TSPackage auth, CK.Ng.Cris.AspNet.TSPackage ngCris ) { }
}
