using CK.Core;
using CK.Ng.AspNet.Auth;
using CK.TS.Angular;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Auth;

[TypeScriptPackage]
[Requires<AspNetAuthPackage, CrisAspNetPackage>]
[NgProviderImport( "provideNgCrisAspNetAuthSupport" )]
[NgProvider( "provideNgCrisAspNetAuthSupport()", "#Support" )]
[TypeScriptFile( "cris-aspnet-auth-providers.ts", "provideNgCrisAspNetAuthSupport" )]
public class CrisAspNetAuthPackage : TypeScriptPackage
{
}
