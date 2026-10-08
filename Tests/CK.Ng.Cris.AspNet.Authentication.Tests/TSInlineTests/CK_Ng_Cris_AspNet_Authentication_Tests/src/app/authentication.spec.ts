import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { AuthService, AuthLevel, HttpCrisEndpoint, BasicLoginCommand, LogoutSessionCommand } from '@local/ck-gen';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';
import { BasicLoginForm } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/Basic/basic-login-form/basic-login-form';

// Trick from https://stackoverflow.com/a/77047461/190380
if( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout(20 * 60 * 1000 ); // 20 minutes

function login( userName: string, password: string ): BasicLoginCommand {
  const c = new BasicLoginCommand();
  c.userName = userName;
  c.password = password;
  return c;
}

describe( 'NgCrisAuthenticationPackage', () => {
  let cris: HttpCrisEndpoint;
  let auth: AuthService;
  let ngAuth: NgAuthService;

  beforeEach( async () => {
    localStorage.clear();
    await TestBed.configureTestingModule( {
      providers: [...CKGenAppModule.Providers, provideNoopAnimations()]
    } ).compileComponents();
    cris = TestBed.inject( HttpCrisEndpoint );
    // Starts anonymous (the cookies of the previous test are cleared by the logout).
    await cris.sendAsync( new LogoutSessionCommand() );
    auth = TestBed.inject( AuthService );
    await auth.isInitialized;
    ngAuth = TestBed.inject( NgAuthService );
  } );

  afterEach( () => auth.close() );

  it( 'provides the AuthService on the HttpCrisEndpoint', () => {
    expect( ngAuth.authService ).toBe( auth );
    expect( ngAuth.actor().actorId ).toBe( 0 );
    expect( ngAuth.level() ).toBe( AuthLevel.None );
  } );

  it( 'the signals follow a login sent through the HttpCrisEndpoint', async () => {
    const r = await cris.sendOrThrowAsync( login( "Albert", "success" ) );
    expect( r?.success ).toBe( true );
    expect( ngAuth.actor().actorName ).toBe( "Albert" );
    expect( ngAuth.level() ).toBe( AuthLevel.Normal );
    expect( ngAuth.isImpersonated() ).toBe( false );
    // The ambient values follow the authentication.
    const a = await cris.updateAmbientValuesAsync();
    expect( ( a as any ).actorId ).toBe( 3712 );
  } );

  it( 'the BasicLoginForm logs in', async () => {
    const fixture = TestBed.createComponent( BasicLoginForm );
    const form = fixture.componentInstance;
    form.loginForm.setValue( { userName: "Albert", password: "success", rememberMe: true } );
    await form.submit();
    expect( ngAuth.actor().actorName ).toBe( "Albert" );
    expect( auth.rememberMe ).toBe( true );
  } );

  it( 'the BasicLoginForm with wrong credentials stays anonymous', async () => {
    const fixture = TestBed.createComponent( BasicLoginForm );
    const form = fixture.componentInstance;
    form.loginForm.setValue( { userName: "Albert", password: "failure", rememberMe: false } );
    await form.submit();
    expect( ngAuth.actor().actorId ).toBe( 0 );
  } );

  it( 'logout goes back to the anonymous', async () => {
    await cris.sendOrThrowAsync( login( "Albert", "success" ) );
    expect( ngAuth.actor().actorId ).toBe( 3712 );
    await auth.logout();
    expect( ngAuth.actor().actorId ).toBe( 0 );
  } );
} );
