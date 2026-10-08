using CK.Core;
using CK.Cris.AspNet.Authentication;
using CK.Ng.Cris.AspNet;
using CK.Ng.Zorro;
using CK.TS.Angular;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Authentication;

/// <summary>
/// The Angular front of the authentication: it provides the framework-free AuthService (on the HttpCrisEndpoint),
/// waits for its initialization at startup and exposes the <c>NgAuthService</c> signals.
/// <para>
/// The authentication applies to the Cris commands only: the bearer token is accepted with the binding cookie,
/// which is sent only to "/.cris". There is no HttpClient interceptor.
/// </para>
/// <para>
/// This package coexists with the old CK.Ng.AspNet.Auth but an application uses one of them: their private page and
/// user info box are single components (<see cref="INgPrivatePageComponent"/>, <see cref="INgUserInfoBoxComponent"/>).
/// </para>
/// </summary>
[TypeScriptPackage]
[Requires<CrisAspNetAuthenticationPackage, CrisAspNetPackage, ZorroPackage>]
[NgProviderImport( "inject", From = "@angular/core" )]
[NgProviderImport( "AuthService, HttpCrisEndpoint" )]
[NgProviderImport( "provideNgCrisAuthenticationSupport", From = "@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/authentication-support" )]
[NgProvider( "{ provide: AuthService, useFactory: () => new AuthService( inject( HttpCrisEndpoint ) ) }" )]
[NgProvider( "provideNgCrisAuthenticationSupport()", "#Support" )]
[TypeScriptFile( "NgAuthService.ts", "NgAuthService" )]
public class NgCrisAuthenticationPackage : TypeScriptPackage
{
}
