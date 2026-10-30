import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { AuthService, AuthLevel, HttpCrisEndpoint, BasicLoginCommand, LogoutSessionCommand } from '@local/ck-gen';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';
import { Login } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/login/login';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BasicLoginForm } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/Basic/basic-login-form/basic-login-form';
import { WeakLoginForm } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/Weak/weak-login-form/weak-login-form';

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

describe( 'Login', () => {
  beforeEach( async () => {
    localStorage.clear();
    await TestBed.configureTestingModule( {
      providers: [...CKGenAppModule.Providers, provideNoopAnimations(), provideRouter( [] )]
    } ).compileComponents();
    await TestBed.inject( HttpCrisEndpoint ).sendAsync( new LogoutSessionCommand() );
    await TestBed.inject( AuthService ).isInitialized;
  } );

  afterEach( () => TestBed.inject( AuthService ).close() );

  it( 'shows a button per remote scheme', async () => {
    const fixture = TestBed.createComponent( Login );
    fixture.autoDetectChanges();
    // ngOnInit reads the schemes (an async command).
    for( let i = 0; i < 100 && fixture.componentInstance.remoteSchemes().length === 0; ++i ) await new Promise( r => setTimeout( r, 20 ) );
    await fixture.whenStable();
    expect( fixture.componentInstance.remoteSchemes() ).toEqual( ["Oidc.Google", "Oidc.Microsoft"] );
    const buttons = ( fixture.nativeElement as HTMLElement ).querySelectorAll( "button.ck-login-provider-btn" );
    expect( buttons.length ).toBe( 2 );
    expect( buttons[0].textContent ).toContain( "Google" );
    expect( fixture.componentInstance.getProviderLogoSrc( "Oidc.Google" ) ).toBe( "login-providers/google.png" );
  } );

  it( 'contains the injected login forms', async () => {
    const fixture = TestBed.createComponent( Login );
    fixture.detectChanges();
    const e = fixture.nativeElement as HTMLElement;
    expect( e.querySelector( "ck-basic-login-form" ) ).not.toBeNull();
    expect( e.querySelector( "ck-weak-login-form" ) ).not.toBeNull();
  } );
} );

describe( 'The Critical level', () => {
  let cris: HttpCrisEndpoint;
  let auth: AuthService;
  let ngAuth: NgAuthService;
  let navigated: Array<unknown>;
  // The query parameters of the login page: "/auth?critical" (a test can change them before creating the page).
  let queryParams: Record<string, string>;

  beforeEach( async () => {
    localStorage.clear();
    queryParams = { critical: 'true' };
    await TestBed.configureTestingModule( {
      providers: [...CKGenAppModule.Providers,
                  provideNoopAnimations(),
                  provideRouter( [] ),
                  { provide: ActivatedRoute, useValue: { get snapshot() { return { queryParams, queryParamMap: convertToParamMap( queryParams ) }; } } }]
    } ).compileComponents();
    cris = TestBed.inject( HttpCrisEndpoint );
    await cris.sendAsync( new LogoutSessionCommand() );
    auth = TestBed.inject( AuthService );
    await auth.isInitialized;
    ngAuth = TestBed.inject( NgAuthService );
    navigated = [];
    jest.spyOn( TestBed.inject( Router ), 'navigate' ).mockImplementation( async ( c, e ) => { navigated.push( [c, e?.queryParams] ); return true; } );
  } );

  afterEach( () => auth.close() );

  it( 'the user can reach it with the schemes it has that can elevate it', async () => {
    await ngAuth.refreshSchemesAsync();
    expect( ngAuth.canReachCritical() ).toBe( false );
    // A Basic login is Normal here (the fake authentication center): "Basic" can elevate Albert.
    await cris.sendOrThrowAsync( login( "Albert", "success" ) );
    expect( ngAuth.level() ).toBe( AuthLevel.Normal );
    expect( ngAuth.criticalSchemes() ).toEqual( ["Basic"] );
    expect( ngAuth.canReachCritical() ).toBe( true );
    // By default, the login page is in a popup: the current page keeps its state.
    const elevation = jest.spyOn( auth, 'startPopupElevation' ).mockResolvedValue( true );
    expect( await ngAuth.elevateToCritical() ).toBe( true );
    expect( elevation ).toHaveBeenCalledWith( "/auth?critical=true&popup=true" );
    expect( navigated ).toEqual( [] );
    // The inline mode navigates to the login page, that comes back to the current page.
    await ngAuth.elevateToCritical( true );
    expect( navigated ).toEqual( [[['/auth'], { critical: true, redirectTo: "/" }]] );
  } );

  it( 'the level follows the end of the Critical level', async () => {
    await cris.sendOrThrowAsync( login( "Albert", "critical-1s" ) );
    expect( ngAuth.level() ).toBe( AuthLevel.Critical );
    await new Promise( r => setTimeout( r, 1500 ) );
    expect( ngAuth.level() ).toBe( AuthLevel.Normal );
  } );

  it( 'the login page proposes only these schemes and stays until the Critical level', async () => {
    await cris.sendOrThrowAsync( login( "Albert", "success" ) );
    const fixture = TestBed.createComponent( Login );
    fixture.autoDetectChanges();
    for( let i = 0; i < 100 && ngAuth.availableSchemes().length === 0; ++i ) await new Promise( r => setTimeout( r, 20 ) );
    await fixture.whenStable();
    expect( fixture.componentInstance.critical() ).toBe( true );
    expect( navigated ).toEqual( [] );
    const e = fixture.nativeElement as HTMLElement;
    expect( e.querySelector( "ck-basic-login-form" ) ).not.toBeNull();
    expect( e.querySelector( "ck-weak-login-form" ) ).toBeNull();
    // Albert has no remote scheme.
    expect( fixture.componentInstance.remoteSchemes() ).toEqual( [] );
  } );

  it( 'in the popup of an elevation, the login page does not redirect: its opener closes it', async () => {
    queryParams = { critical: 'true', popup: 'true' };
    Object.defineProperty( window, 'opener', { value: { closed: false }, configurable: true } );
    try {
      await cris.sendOrThrowAsync( login( "Albert", "success" ) );
      const fixture = TestBed.createComponent( Login );
      fixture.autoDetectChanges();
      await fixture.whenStable();
      expect( fixture.componentInstance.popup ).toBe( true );
      await cris.sendOrThrowAsync( login( "Albert", "critical-1s" ) );
      expect( ngAuth.level() ).toBe( AuthLevel.Critical );
      await fixture.whenStable();
      expect( navigated ).toEqual( [] );
    }
    finally {
      Object.defineProperty( window, 'opener', { value: null, configurable: true } );
    }
  } );
} );

describe( 'WeakLoginForm', () => {
  let auth: AuthService;
  let ngAuth: NgAuthService;
  let navigated: Array<unknown>;

  beforeEach( async () => {
    localStorage.clear();
    await TestBed.configureTestingModule( {
      providers: [...CKGenAppModule.Providers, provideNoopAnimations(), provideRouter( [] )]
    } ).compileComponents();
    await TestBed.inject( HttpCrisEndpoint ).sendAsync( new LogoutSessionCommand() );
    auth = TestBed.inject( AuthService );
    await auth.isInitialized;
    ngAuth = TestBed.inject( NgAuthService );
    navigated = [];
    jest.spyOn( TestBed.inject( Router ), 'navigate' ).mockImplementation( async c => { navigated.push( c ); return true; } );
  } );

  afterEach( () => auth.close() );

  it( 'logs in a weak actor: it stays the anonymous (Unsafe level) and is redirected', async () => {
    const fixture = TestBed.createComponent( WeakLoginForm );
    fixture.componentRef.setInput( 'redirectTo', '/operator' );
    fixture.componentInstance.loginForm.setValue( { weakActorName: "Operator" } );
    await fixture.componentInstance.submit();
    expect( auth.authenticationInfo.unsafeActor.actorName ).toBe( "Operator" );
    expect( auth.authenticationInfo.unsafeActor.level ).toBe( AuthLevel.Unsafe );
    expect( ngAuth.actor().actorId ).toBe( 0 );
    expect( navigated ).toEqual( [['/operator']] );
  } );

  it( 'an unknown weak actor stays where it is', async () => {
    const fixture = TestBed.createComponent( WeakLoginForm );
    fixture.componentInstance.loginForm.setValue( { weakActorName: "Nobody" } );
    await fixture.componentInstance.submit();
    expect( auth.authenticationInfo.unsafeActor.actorId ).toBe( 0 );
    expect( navigated ).toEqual( [] );
  } );
} );
