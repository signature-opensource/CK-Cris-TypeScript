import axios from "axios";
import { HttpCrisEndpoint, AuthService, AuthLevel, ICommand, SessionResult } from "@local/ck-gen";
import { WeakLoginCommand, BasicLoginCommand, WhoAmIQCommand, LogoutSessionCommand, RefreshSessionCommand, StartLoginCommand, RedirectedUrl } from "@local/ck-gen";

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

it( 'a logout sent while a refresh is in flight wins, whatever the order of the answers', async () => {
  const [ep, auth] = await newTabAsync();
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  for( let i = 0; i < 5; ++i ) {
    await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
    await Promise.all( [auth.refresh(), auth.logout()] );
    expect( auth.token ).toBeUndefined();
    expect( localStorage.getItem( "ck-auth" ) ).toBeNull();
    expect( await whoAmIAsync( ep ) ).toBe( "Anonymous" );
  }
});

it( 'the login schemes are the ones of the authentication center', async () => {
  const [, auth] = await newTabAsync();
  expect( auth.availableSchemes ).toEqual( [] );
  const schemes = await auth.refreshSchemesAsync();
  expect( schemes.map( s => s.name ) ).toEqual( ["Oidc.Test", "Weak"] );
  // The Weak scheme cannot elevate an actor to the Critical level.
  expect( schemes.map( s => s.criticalLifetime.toMillis() ) ).toEqual( [5 * 60 * 1000, 0] );
  expect( auth.availableSchemes.map( s => s.name ) ).toEqual( ["Oidc.Test", "Weak"] );
  // The anonymous cannot reach the Critical level.
  expect( auth.criticalSchemes ).toEqual( [] );
});

it( 'a Critical remote login asks the provider to authenticate the user again', async () => {
  const frame = document.createElement( "iframe" );
  document.body.appendChild( frame );
  const popup = frame.contentWindow!;
  const open = jest.spyOn( window, "open" ).mockReturnValue( popup );
  try {
    const [, auth, url] = await newRemoteLoginTabAsync();
    const login = auth.startPopupLogin( "Oidc.Test", false, undefined, true );
    while( !url() ) await new Promise( r => setTimeout( r, 10 ) );
    expect( url() ).toContain( "prompt=login" );
    window.dispatchEvent( new MessageEvent( "message", { data: { type: "ck-auth-remote-login", success: false, error: "Authentication.RemoteLoginRefused" }, origin: window.location.origin, source: popup } ) );
    expect( await login ).toBe( false );
  }
  finally {
    open.mockRestore();
    frame.remove();
  }
});

it( 'back from an inline remote login, the session is refreshed (the cached token is stale)', async () => {
  const [ep1] = await newTabAsync();
  await ep1.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  // The session rotates without updating the cached token (what the remote login callback does: new cookies).
  await new HttpCrisEndpoint( axios, crisEndpoint ).sendAsync( new RefreshSessionCommand() );
  // Without the marker, a new page adopts the stale cached token: it is anonymous.
  const [ep2] = await newTabAsync();
  expect( await whoAmIAsync( ep2 ) ).toBe( "Anonymous" );
  // With the marker, the new page refreshes: the session is the new one.
  window.history.pushState( null, "", "/page?x=1&ck-auth-login=ok" );
  const sent: ICommand<unknown>[] = [];
  const [ep3, auth3] = await newTabAsync( sent );
  expect( sent.some( c => c instanceof RefreshSessionCommand ) ).toBe( true );
  expect( window.location.search ).toBe( "?x=1" );
  expect( auth3.remoteLoginError ).toBeUndefined();
  expect( await whoAmIAsync( ep3 ) ).toBe( "Operator" );
});

it( 'the error of an inline remote login is available', async () => {
  window.history.pushState( null, "", "/page?ck-auth-login=Test.Refused" );
  const [, auth] = await newTabAsync();
  expect( auth.remoteLoginError ).toBe( "Test.Refused" );
  expect( window.location.search ).toBe( "" );
});

it( 'a blocked popup fails the remote login', async () => {
  const open = jest.spyOn( window, "open" ).mockReturnValue( null );
  try {
    const [, auth] = await newTabAsync();
    expect( await auth.startPopupLogin( "Oidc.Test" ) ).toBe( false );
    expect( auth.remoteLoginError ).toBe( "Authentication.PopupBlocked" );
  }
  finally {
    open.mockRestore();
  }
});

// A tab that records the provider's URL returned to a StartLoginCommand (assigned to the popup).
async function newRemoteLoginTabAsync(): Promise<[HttpCrisEndpoint, AuthService, () => string]> {
  const ep = new HttpCrisEndpoint( axios, crisEndpoint );
  let url = "";
  ep.addHook( { onExecuted: e => { if( e.command instanceof StartLoginCommand && e.result instanceof RedirectedUrl ) url = e.result.url; } } );
  const a = await AuthService.createAsync( ep );
  services.push( a );
  return [ep, a, () => url];
}

it( 'a remote login in a popup', async () => {
  // The "popup" is an iframe: its window is the source of the message posted by the callback page.
  const frame = document.createElement( "iframe" );
  document.body.appendChild( frame );
  const popup = frame.contentWindow!;
  const open = jest.spyOn( window, "open" ).mockReturnValue( popup );
  try {
    const [ep, auth, url] = await newRemoteLoginTabAsync();
    const login = auth.startPopupLogin( "Oidc.Test" );
    while( !url() ) await new Promise( r => setTimeout( r, 10 ) );
    const state = url().substring( url().indexOf( "state=" ) + 6 );
    // The provider redirects the popup to the callback: the browser presents the remote login cookie, the session is issued.
    const callback = await axios.get( "/.auth/oidc/callback?state=" + state + "&code=the-code", { baseURL: window.location.origin, responseType: "text" } );
    expect( callback.data ).toContain( '"success":true' );
    // The callback page posts its message to the opener.
    window.dispatchEvent( new MessageEvent( "message", { data: { type: "ck-auth-remote-login", success: true, error: null }, origin: window.location.origin, source: popup } ) );
    expect( await login ).toBe( true );
    expect( auth.token ).toBeDefined();
    expect( await whoAmIAsync( ep ) ).toBe( "Operator" );
  }
  finally {
    open.mockRestore();
    frame.remove();
  }
});

it( 'a message that does not come from the popup is ignored', async () => {
  const frame = document.createElement( "iframe" );
  document.body.appendChild( frame );
  const popup = frame.contentWindow!;
  const open = jest.spyOn( window, "open" ).mockReturnValue( popup );
  try {
    const [, auth, url] = await newRemoteLoginTabAsync();
    const login = auth.startPopupLogin( "Oidc.Test" );
    while( !url() ) await new Promise( r => setTimeout( r, 10 ) );
    // Another window (or origin) cannot end the login.
    window.dispatchEvent( new MessageEvent( "message", { data: { type: "ck-auth-remote-login", success: true }, origin: window.location.origin, source: window } ) );
    window.dispatchEvent( new MessageEvent( "message", { data: { type: "ck-auth-remote-login", success: true }, origin: "https://evil.example.com", source: popup } ) );
    // The popup's own failure ends the login (if one of the messages above had been accepted, the login would succeed).
    // (The closing of the popup is polled: jsdom doesn't close an iframe's window, it cannot be tested here.)
    window.dispatchEvent( new MessageEvent( "message", { data: { type: "ck-auth-remote-login", success: false, error: "Authentication.RemoteLoginRefused" }, origin: window.location.origin, source: popup } ) );
    expect( await login ).toBe( false );
    expect( auth.remoteLoginError ).toBe( "Authentication.RemoteLoginRefused" );
    expect( auth.token ).toBeUndefined();
  }
  finally {
    open.mockRestore();
    frame.remove();
  }
});

// Makes the next refreshes fail like a network error (the request never reaches the server). Returns the interceptor
// to eject and a function that tells how many failures are left.
function failNextRefreshes( count: number ): [number, () => number] {
  let left = count;
  const id = axios.interceptors.request.use( config => {
    if( left > 0 && String( config.data ).includes( "RefreshSessionCommand" ) ) {
      --left;
      throw new Error( "Network down." );
    }
    return config;
  } );
  return [id, () => left];
}

async function waitForAsync( condition: () => boolean, ms: number ): Promise<boolean> {
  for( let t = 0; t < ms; t += 50 ) {
    if( condition() ) return true;
    await new Promise( r => setTimeout( r, 50 ) );
  }
  return condition();
}

it( 'a refresh that cannot reach the server keeps the session and is retried', async () => {
  const [ep, auth] = await newTabAsync();
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  const token = auth.token;
  const [id, left] = failNextRefreshes( 1 );
  try {
    await auth.refresh();
    expect( left() ).toBe( 0 );
    // Nothing has been decided: the session is kept (a failed SessionResult would have ended it).
    expect( auth.token ).toBe( token );
    expect( auth.authenticationInfo.unsafeActor.actorName ).toBe( "Operator" );
    // The refresh is retried after 1 second.
    expect( await waitForAsync( () => auth.token !== token, 4000 ) ).toBe( true );
    expect( await whoAmIAsync( ep ) ).toBe( "Operator" );
  }
  finally {
    axios.interceptors.request.eject( id );
  }
});

it( 'the retries back off and coming back online retries at once', async () => {
  const [ep, auth] = await newTabAsync();
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  const token = auth.token;
  const [id, left] = failNextRefreshes( 2 );
  try {
    await auth.refresh();
    // The first retry (1 s) fails too: the next one is in 2 seconds.
    expect( await waitForAsync( () => left() === 0, 3000 ) ).toBe( true );
    expect( auth.token ).toBe( token );
    // The browser comes back online: retried at once.
    window.dispatchEvent( new Event( "online" ) );
    expect( await waitForAsync( () => auth.token !== token, 1000 ) ).toBe( true );
    expect( await whoAmIAsync( ep ) ).toBe( "Operator" );
  }
  finally {
    axios.interceptors.request.eject( id );
  }
});

it( 'a refresh refused by the server ends the session and is not retried', async () => {
  const sent: ICommand<unknown>[] = [];
  const [ep, auth] = await newTabAsync( sent );
  await ep.sendOrThrowAsync( new WeakLoginCommand( "Operator" ) );
  // Another client ends the session: the cookies are gone, the next refresh is refused (a decision).
  await new HttpCrisEndpoint( axios, crisEndpoint ).sendAsync( new LogoutSessionCommand() );
  await auth.refresh();
  expect( auth.token ).toBeUndefined();
  const refreshes = sent.filter( c => c instanceof RefreshSessionCommand ).length;
  await new Promise( r => setTimeout( r, 1500 ) );
  expect( sent.filter( c => c instanceof RefreshSessionCommand ).length ).toBe( refreshes );
});

it( 'a binding in a popup leaves the session unchanged (it keeps its Critical level)', async () => {
  const frame = document.createElement( "iframe" );
  document.body.appendChild( frame );
  const popup = frame.contentWindow!;
  const open = jest.spyOn( window, "open" ).mockReturnValue( popup );
  try {
    const [ep, auth, url] = await newRemoteLoginTabAsync();
    await ep.sendOrThrowAsync( new BasicLoginCommand( "Albert", "success" ) );
    expect( auth.level ).toBe( AuthLevel.Critical );
    const token = auth.token;
    const sent: ICommand<unknown>[] = [];
    ep.addHook( { onExecuted: e => { sent.push( e.command ); } } );
    const binding = auth.startPopupBinding( "Oidc.Test" );
    while( !url() ) await new Promise( r => setTimeout( r, 10 ) );
    const state = url().substring( url().indexOf( "state=" ) + 6 );
    const callback = await axios.get( "/.auth/oidc/callback?state=" + state + "&code=the-code", { baseURL: window.location.origin, responseType: "text" } );
    expect( callback.data ).toContain( '"success":true' );
    expect( callback.data ).toContain( '"bound":true' );
    window.dispatchEvent( new MessageEvent( "message", { data: { type: "ck-auth-remote-login", success: true, bound: true, error: null }, origin: window.location.origin, source: popup } ) );
    expect( await binding ).toBe( true );
    expect( auth.remoteLoginBound ).toBe( true );
    expect( auth.remoteLoginError ).toBeUndefined();
    expect( sent.some( c => c instanceof RefreshSessionCommand ) ).toBe( false );
    expect( auth.token ).toBe( token );
    expect( auth.level ).toBe( AuthLevel.Critical );
    expect( await whoAmIAsync( ep ) ).toBe( "Albert" );
  }
  finally {
    open.mockRestore();
    frame.remove();
  }
});

it( 'an anonymous binding is refused at its start', async () => {
  const frame = document.createElement( "iframe" );
  document.body.appendChild( frame );
  const popup = frame.contentWindow!;
  const open = jest.spyOn( window, "open" ).mockReturnValue( popup );
  try {
    const [, auth] = await newRemoteLoginTabAsync();
    expect( await auth.startPopupBinding( "Oidc.Test" ) ).toBe( false );
    expect( auth.remoteLoginBound ).toBe( false );
    expect( auth.remoteLoginError ).toBeDefined();
  }
  finally {
    open.mockRestore();
    frame.remove();
  }
});

it( 'back from an inline binding, the session is not refreshed', async () => {
  const [ep1, auth1] = await newTabAsync();
  await ep1.sendOrThrowAsync( new BasicLoginCommand( "Albert", "success" ) );
  expect( auth1.level ).toBe( AuthLevel.Critical );
  window.history.pushState( null, "", "/page?ck-auth-login=bound" );
  const sent: ICommand<unknown>[] = [];
  const [ep2, auth2] = await newTabAsync( sent );
  expect( sent.some( c => c instanceof RefreshSessionCommand ) ).toBe( false );
  expect( window.location.search ).toBe( "" );
  expect( auth2.remoteLoginBound ).toBe( true );
  expect( auth2.remoteLoginError ).toBeUndefined();
  expect( auth2.level ).toBe( AuthLevel.Critical );
  expect( await whoAmIAsync( ep2 ) ).toBe( "Albert" );
});
