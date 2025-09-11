using CK.Core;
using CK.TS.Angular;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Auth;

[TypeScriptPackage]
[Requires<CK.Ng.AspNet.Auth.TSPackage, CK.Ng.Cris.AspNet.TSPackage>]
[NgProviderImport( "provideNgCrisAspNetAuthSupport" )]
[NgProvider( "provideNgCrisAspNetAuthSupport()", "#Support" )]
[TypeScriptFile( "cris-aspnet-auth-providers.ts", "provideNgCrisAspNetAuthSupport" )]
public class TSPackage : TypeScriptPackage
{
}
