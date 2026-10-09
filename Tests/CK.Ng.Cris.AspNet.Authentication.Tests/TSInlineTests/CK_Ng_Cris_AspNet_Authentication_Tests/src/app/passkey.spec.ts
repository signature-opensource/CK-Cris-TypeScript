import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';
import { AuthService, AuthLevel, HttpCrisEndpoint, LogoutSessionCommand, StartLoginCommand } from '@local/ck-gen';
import { setLoginUserData } from '@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService';
import { PasskeyService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/Passkey/PasskeyService';

if( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout( 20 * 60 * 1000 );

// jsdom has no WebAuthn: the browser's part is mocked. The JSON helpers return what they receive, the authenticator
// "signs" the challenge of the options it is given (the fake server checks it).
type Behavior = 'sign' | 'cancel' | 'already-registered';

function mockWebAuthn( behavior: Behavior ): void {
  ( globalThis as any ).PublicKeyCredential = class {
    static parseRequestOptionsFromJSON( o: any ) { return o; }
    static parseCreationOptionsFromJSON( o: any ) { return o; }
  };
  const answer = async ( options: any ) => {
    if( behavior === 'cancel' ) throw new DOMException( 'The operation either timed out or was not allowed.', 'NotAllowedError' );
    if( behavior === 'already-registered' ) throw new DOMException( 'The authenticator was previously registered.', 'InvalidStateError' );
    return { toJSON: () => ( { id: 'the-credential', signed: options.publicKey.challenge } ) };
  };
  Object.defineProperty( navigator, 'credentials', { configurable: true, value: { get: answer, create: answer } } );
}

function removeWebAuthn(): void {
  delete ( globalThis as any ).PublicKeyCredential;
  Object.defineProperty( navigator, 'credentials', { configurable: true, value: undefined } );
}

describe( 'PasskeyService', () => {
  let cris: HttpCrisEndpoint;
  let auth: AuthService;

  beforeEach( async () => {
    localStorage.clear();
    await TestBed.configureTestingModule( {
      providers: [...CKGenAppModule.Providers, provideNoopAnimations()]
    } ).compileComponents();
    cris = TestBed.inject( HttpCrisEndpoint );
    await cris.sendAsync( new LogoutSessionCommand() );
    auth = TestBed.inject( AuthService );
    await auth.isInitialized;
  } );

  afterEach( () => { auth.close(); removeWebAuthn(); } );

  it( 'is not supported without the WebAuthn JSON helpers', async () => {
    removeWebAuthn();
    expect( PasskeyService.isSupported ).toBe( false );
    const outcome = await new PasskeyService( cris ).loginAsync();
    expect( outcome.error ).toBe( 'Authentication.PasskeyNotSupported' );
  } );

  it( 'logs in: the AuthService follows the session', async () => {
    mockWebAuthn( 'sign' );
    expect( PasskeyService.isSupported ).toBe( true );
    const outcome = await new PasskeyService( cris ).loginAsync( true );
    expect( outcome.cancelled ).toBe( false );
    expect( outcome.error ).toBeUndefined();
    expect( outcome.result?.success ).toBe( true );
    expect( auth.actor.actorName ).toBe( 'Albert' );
    expect( auth.level ).toBe( AuthLevel.Critical );
    expect( auth.rememberMe ).toBe( true );
  } );

  it( 'registration requires the Critical level', async () => {
    mockWebAuthn( 'sign' );
    const outcome = await new PasskeyService( cris ).registerAsync();
    expect( outcome.result ).toBeUndefined();
    expect( outcome.error ).toBe( 'Invalid authentication level: Critical authentication level required.' );
  } );

  it( 'a cancelled ceremony is not an error and sends nothing', async () => {
    mockWebAuthn( 'cancel' );
    const outcome = await new PasskeyService( cris ).loginAsync();
    expect( outcome.cancelled ).toBe( true );
    expect( outcome.error ).toBeUndefined();
    expect( auth.actor.actorId ).toBe( 0 );
  } );

  it( 'registers a new passkey from a Critical session', async () => {
    mockWebAuthn( 'sign' );
    const passkeys = new PasskeyService( cris );
    expect( ( await passkeys.loginAsync() ).result?.success ).toBe( true );
    // The command's ActorId is the ambient value: the endpoint follows the authentication.
    await cris.updateAmbientValuesAsync();
    const outcome = await passkeys.registerAsync( 'My phone' );
    expect( outcome.result?.success ).toBe( true );
    expect( outcome.result?.name ).toBe( 'Test passkey' );
  } );

  it( 'an authenticator that already holds a passkey of the actor is reported', async () => {
    mockWebAuthn( 'sign' );
    const passkeys = new PasskeyService( cris );
    expect( ( await passkeys.loginAsync() ).result?.success ).toBe( true );
    await cris.updateAmbientValuesAsync();
    mockWebAuthn( 'already-registered' );
    const outcome = await passkeys.registerAsync();
    expect( outcome.cancelled ).toBe( false );
    expect( outcome.error ).toBe( 'Authentication.PasskeyExists' );
  } );
} );

describe( 'Login user data', () => {
  it( 'are copied from a record or a map', () => {
    const c = new StartLoginCommand();
    setLoginUserData( c.userData, { InvitationToken: 'T' } );
    setLoginUserData( c.userData, new Map( [['Other', 'O']] ) );
    setLoginUserData( c.userData, undefined );
    expect( [...c.userData.entries()] ).toEqual( [['InvitationToken', 'T'], ['Other', 'O']] );
  } );
} );
