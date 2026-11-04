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
// The culture of the commands follows the locale of the application (LocaleService.setLocale): without it, the commands
// keep the culture that the server collected with the ambient values.
[NgProviderImport( "LocaleService", From = "@local/ck-gen/ts-locales/locales" )]
[NgProviderImport( "effect, provideAppInitializer", From = "@angular/core" )]
[NgProvider( """
             provideAppInitializer( () => {
               const cris = inject( HttpCrisEndpoint );
               const locale = inject( LocaleService );
               effect( () => { cris.ambientValuesOverride.currentCultureName = locale.currentLocale(); } );
             } )
             """, "CultureFollowsLocale" )]
public class CrisAspNetPackage : TypeScriptPackage
{
}
