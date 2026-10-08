import axios from "axios";
import { HttpCrisEndpoint, AuthService, AuthLevel, ICommand, SessionResult } from "@local/ck-gen";
import { WeakLoginCommand, WhoAmIQCommand, LogoutSessionCommand, RefreshSessionCommand } from "@local/ck-gen";

// Trick from https://stackoverflow.com/a/77047461/190380
if( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout(20 * 60 * 1000 ); // 20 minutes

const crisEndpoint = CKTypeScriptEnv["CRIS_ENDPOINT_URL"] ?? "";

// Each AuthService is a "tab": they share the cookies and the localStorage of the jsdom window.
let services: AuthService[] = [];

async function newTabAsync( sent?: ICommand<unknown>[] ): Promise<[HttpCrisEndpoint, AuthService]> {
  const ep = new HttpCrisEndpoint( axios, crisEndpoint );
  if( sent ) ep.addHook( { onExecuted: e => { sent.push( e.command ); } } );
  const a = await AuthService.createAsync( ep );
  services.push( a );
  return [ep, a];
}

async function whoAmIAsync( ep: HttpCrisEndpoint ): Promise<string> {
  return (await ep.sendOrThrowAsync( new WhoAmIQCommand() )) ?? "";
}

beforeEach( async () => {
  // Starts anonymous: no session cookies, no cached token.
  await new HttpCrisEndpoint( axios, crisEndpoint ).sendAsync( new LogoutSessionCommand() );
  localStorage.clear();
});

afterEach( () => {
  services.forEach( s => s.close() );
  services = [];
});

it( 'starts anonymous', async () => {
  const [ep, auth] = await newTabAsync();
  expect( auth.token ).toBeUndefined();
  expect( auth.level ).toBe( AuthLevel.None );
  expect( await whoAmIAsync( ep ) ).toBe( "Anonymous" );
});

it( 'a login sent through the endpoint updates the AuthService', async () => {
  const [ep, auth] = await newTabAsync();
  let changed = 0;
  auth.addOnChange( () => ++changed );
  // No AuthService method: the session command is sent like any other command.
  const r = (await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) ))!;
  expect( r.success ).toBe( true );
  expect( changed ).toBe( 1 );
  expect( auth.token ).toBe( r.token );
  expect( auth.authenticationInfo.unsafeActor.actorName ).toBe( "Operator" );
  // A weak login is Unsafe: the (safe) actor is the anonymous.
  expect( auth.level ).toBe( AuthLevel.Unsafe );
  expect( auth.actor.actorId ).toBe( 0 );
  // The bearer (with the binding cookie) authenticates the next commands.
  expect( await whoAmIAsync( ep ) ).toBe( "Operator" );
});

it( 'a failed login changes nothing', async () => {
  const [ep, auth] = await newTabAsync();
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  const token = auth.token;
  const r = (await ep.sendOrThrowAsync( new WeakLoginCommand( "Nobody" ) ))!;
  expect( r.success ).toBe( false );
  expect( auth.token ).toBe( token );
  expect( await whoAmIAsync( ep ) ).toBe( "Operator" );
});

it( 'refresh rotates the token and the session goes on', async () => {
  const [ep, auth] = await newTabAsync();
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  const token = auth.token;
  await auth.refresh();
  expect( auth.token ).toBeDefined();
  expect( auth.token ).not.toBe( token );
  expect( await whoAmIAsync( ep ) ).toBe( "Operator" );
});

it( 'logout ends the session and clears the cached token', async () => {
  const [ep, auth] = await newTabAsync();
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  expect( localStorage.getItem( "ck-auth" ) ).not.toBeNull();
  await auth.logout();
  expect( auth.token ).toBeUndefined();
  expect( auth.level ).toBe( AuthLevel.None );
  expect( localStorage.getItem( "ck-auth" ) ).toBeNull();
  expect( await whoAmIAsync( ep ) ).toBe( "Anonymous" );
  // Without the cookies, a refresh fails.
  await auth.refresh();
  expect( auth.token ).toBeUndefined();
});

it( 'a new page uses the cached token without refreshing', async () => {
  const [ep1, auth1] = await newTabAsync();
  await ep1.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  const sent: ICommand<unknown>[] = [];
  const [ep2, auth2] = await newTabAsync( sent );
  expect( auth2.token ).toBe( auth1.token );
  expect( sent.some( c => c instanceof RefreshSessionCommand ) ).toBe( false );
  expect( await whoAmIAsync( ep2 ) ).toBe( "Operator" );
});

it( 'a page without a cached token refreshes with the cookies', async () => {
  const [ep1] = await newTabAsync();
  await ep1.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  localStorage.clear();
  const [ep2, auth2] = await newTabAsync();
  expect( auth2.token ).toBeDefined();
  expect( await whoAmIAsync( ep2 ) ).toBe( "Operator" );
});

it( 'the tabs share the token: a refresh in a tab does not break the other one', async () => {
  const [ep1, auth1] = await newTabAsync();
  const [ep2, auth2] = await newTabAsync();
  await ep1.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  // In a browser, the storage event is raised in the other tabs only: it is raised here by hand.
  window.dispatchEvent( new StorageEvent( "storage", { key: "ck-auth" } ) );
  expect( auth2.token ).toBe( auth1.token );
  expect( await whoAmIAsync( ep2 ) ).toBe( "Operator" );

  await auth1.refresh();
  // The binding cookie has changed: the old token of tab 2 is useless.
  expect( auth2.token ).not.toBe( auth1.token );
  expect( await whoAmIAsync( ep2 ) ).toBe( "Anonymous" );
  window.dispatchEvent( new StorageEvent( "storage", { key: "ck-auth" } ) );
  expect( auth2.token ).toBe( auth1.token );
  expect( await whoAmIAsync( ep2 ) ).toBe( "Operator" );
});

it( 'a tab that refreshes after another one adopts its token', async () => {
  const [ep1, auth1] = await newTabAsync();
  const sent: ICommand<unknown>[] = [];
  const [ep2, auth2] = await newTabAsync( sent );
  await ep1.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  // Tab 2 has missed the storage event: its refresh adopts the token of tab 1 instead of rotating the secrets again.
  await auth2.refresh();
  expect( auth2.token ).toBe( auth1.token );
  expect( sent.filter( c => c instanceof RefreshSessionCommand ).length ).toBe( 1 ); // The initial one.
  expect( await whoAmIAsync( ep1 ) ).toBe( "Operator" );
  expect( await whoAmIAsync( ep2 ) ).toBe( "Operator" );
});

it( 'the session result is the one of the command', async () => {
  const [ep, auth] = await newTabAsync();
  const executed = await ep.sendAsync( new WeakLoginCommand( "Operator" ) );
  expect( executed.result ).toBeInstanceOf( SessionResult );
  expect( (executed.result as SessionResult).token ).toBe( auth.token );
});
