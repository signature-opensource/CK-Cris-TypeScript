import { TestBed } from '@angular/core/testing';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { HttpCrisEndpoint } from '@local/ck-gen';
import { LocaleService } from '@local/ck-gen/ts-locales/locales';

// CK.Ng.Cris.AspNet: the culture of the commands follows the locale of the application (LocaleService.setLocale).
// Without it, the commands keep the culture that the server collected with the ambient values.
describe( 'The culture of the commands', () => {
  it( 'is the locale of the application', async () => {
    await TestBed.configureTestingModule( { providers: [...CKGenAppModule.Providers] } ).compileComponents();
    const cris = TestBed.inject( HttpCrisEndpoint );
    const locale = TestBed.inject( LocaleService );
    await locale.setLocale( 'en' );
    TestBed.tick();
    expect( cris.ambientValuesOverride.currentCultureName ).toBe( 'en' );
  } );
} );
