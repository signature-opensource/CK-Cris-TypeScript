import { HttpCrisEndpoint, HttpCrisEndpointHook } from '@local/ck-gen/CK/Cris/HttpCrisEndpoint';
import { CrisError, ExecutedCommand, ICommand } from '@local/ck-gen/CK/Cris/Model';
import { CTSType } from '@local/ck-gen/CK/Core/CTSType';
import { AuthLevel } from '@local/ck-gen/CK/Core/AuthLevel';
import { SessionResult } from '@local/ck-gen/CK/IO/Authentication/SessionResult';
import { AuthenticationInfoPoco } from '@local/ck-gen/CK/IO/Authentication/AuthenticationInfoPoco';
import { ActorInfoPoco } from '@local/ck-gen/CK/IO/Authentication/ActorInfoPoco';
import { RefreshSessionCommand } from '@local/ck-gen/CK/IO/Authentication/RefreshSessionCommand';
import { LogoutSessionCommand } from '@local/ck-gen/CK/IO/Authentication/LogoutSessionCommand';
import { ImpersonateCommand } from '@local/ck-gen/CK/IO/Authentication/ImpersonateCommand';
import { ClearImpersonationCommand } from '@local/ck-gen/CK/IO/Authentication/ClearImpersonationCommand';
import { StartLoginCommand } from '@local/ck-gen/CK/IO/Authentication/StartLoginCommand';
import { GetLoginSchemesQCommand } from '@local/ck-gen/CK/IO/Authentication/GetLoginSchemesQCommand';
import { LoginSchemeInfo } from '@local/ck-gen/CK/IO/Authentication/LoginSchemeInfo';
import { RedirectedUrl } from '@local/ck-gen/CK/Cris/RedirectedUrl';

/**
 * The free data of a login (an invitation token for instance): the UserData of the login commands. They come from
 * the client: the server treats them as untrusted, and limits them (16 entries, 4096 characters).
 */
export type LoginUserData = ReadonlyMap<string, string> | Readonly<Record<string, string>>;

/**
 * Copies login user data into the UserData of a login command.
 * @param target The UserData of the command.
 * @param userData The data to copy (nothing when undefined).
 */
export function setLoginUserData( target: Map<string, string>, userData?: LoginUserData ): void {
    if( !userData ) return;
    const entries = userData instanceof Map ? userData.entries() : Object.entries( userData );
    for( const [k, v] of entries ) target.set( k, v );
}

/**
 * The authentication state of the application, on top of a HttpCrisEndpoint.
 * <p>
 * The state is updated by the endpoint itself (a hook): any session command sent through the endpoint (a login,
 * a refresh, a logout...) has its full effect, whoever sends it. The methods of this service are simple helpers.
 * </p>
 * <p>
 * The token is cached in the localStorage and shared by the tabs of the browser: every session command rotates the
 * session secrets (the cookies), so a tab that keeps an old token would be anonymous. The token alone is useless:
 * it is accepted by the server only with the binding cookie of its session (HttpOnly).
 * </p>
 * <p>
 * A refresh whose answer is a decision of the server (a SessionResult) is applied: a failed one ends the session.
 * A refresh that decided nothing (the server could not be reached, or failed: a CommunicationError or an
 * ExecutionError) keeps the session and is retried with a backoff (1 s, 2 s, 4 s... at most 30 s), endlessly: the server
 * answers a decision when the session has really ended. Coming back online or to the tab retries at once. The server
 * accepts the previous secrets once after a rotation whose answer has been lost.
 * </p>
 */
export class AuthService {
    #endpoint: HttpCrisEndpoint;
    #storageKey: string;
    #info: AuthenticationInfoPoco;
    #token: string|undefined;
    #rememberMe: boolean;
    #subscribers: Set<( eventSource: AuthService ) => void>;
    #hook: HttpCrisEndpointHook;
    #onStorage: ( e: StorageEvent ) => void;
    #refreshTimer: ReturnType<typeof setTimeout>|undefined;
    // Notifies the subscribers when the Critical level ends (the level drops to Normal without any session change).
    #criticalTimer: ReturnType<typeof setTimeout>|undefined;
    // The delay of the last retry of a refresh that decided nothing (0: no retry pending).
    #retryDelay: number;
    #onWakeUp: () => void;
    #isInitialized: Promise<void>;
    #closed: boolean;
    #availableSchemes: ReadonlyArray<LoginSchemeInfo>;
    #remoteLoginError: string|undefined;
    #remoteLoginBound: boolean;
    // The sending order of the commands: the last sent session command wins.
    #sent: WeakMap<object, number>;
    #sentCount: number;
    #lastApplied: number;

    /**
     * The name of the lock (Web Locks API) that serializes the refreshes of the tabs.
     */
    public static readonly refreshLockName = "ck-auth-refresh";

    /** The delay before the first retry of a refresh that decided nothing (1 second). */
    public static readonly retryMinDelay = 1000;

    /** The maximal delay between the retries of a refresh that decided nothing (30 seconds). */
    public static readonly retryMaxDelay = 30000;

    /**
     * Creates a new AuthService and waits for its initialization: the cached token is used when it has not
     * expired, otherwise a refresh is sent (the refresh cookie may be valid).
     * @param endpoint The Cris endpoint.
     * @param storageKey The localStorage key of the cached token. Defaults to "ck-auth".
     * @returns The initialized service.
     */
    public static async createAsync( endpoint: HttpCrisEndpoint, storageKey: string = "ck-auth" ): Promise<AuthService> {
        const a = new AuthService( endpoint, storageKey );
        await a.#isInitialized;
        return a;
    }

    /**
     * Initializes a new AuthService. Use createAsync or wait for isInitialized.
     * @param endpoint The Cris endpoint.
     * @param storageKey The localStorage key of the cached token. Defaults to "ck-auth".
     */
    constructor( endpoint: HttpCrisEndpoint, storageKey: string = "ck-auth" ) {
        this.#endpoint = endpoint;
        this.#storageKey = storageKey;
        this.#info = new AuthenticationInfoPoco();
        this.#rememberMe = false;
        this.#closed = false;
        this.#retryDelay = 0;
        this.#availableSchemes = [];
        this.#remoteLoginBound = false;
        this.#sent = new WeakMap();
        this.#sentCount = 0;
        this.#lastApplied = 0;
        this.#subscribers = new Set();
        this.#hook = {
            getHeaders: c => this.#onSending( c ),
            onExecuted: e => this.#onExecuted( e )
        };
        this.#onStorage = e => {
            if( e.key === this.#storageKey ) this.#adopt( this.#readStorage() );
        };
        this.#onWakeUp = () => {
            if( this.#retryDelay && !this.#closed && (typeof document === "undefined" || document.visibilityState !== "hidden") ) {
                this.#clearTimer();
                this.#retryNow();
            }
        };
        endpoint.addHook( this.#hook );
        if( typeof window !== "undefined" ) {
            window.addEventListener( "storage", this.#onStorage );
            window.addEventListener( "online", this.#onWakeUp );
        }
        if( typeof document !== "undefined" ) document.addEventListener( "visibilitychange", this.#onWakeUp );
        this.#isInitialized = this.#initializeAsync();
    }

    /** Gets a promise that resolves once this service is initialized. */
    public get isInitialized(): Promise<void> { return this.#isInitialized; }

    /** Gets whether this service has been closed. */
    public get isClosed(): boolean { return this.#closed; }

    /** Gets the authentication information, as it has been received. See actor and actualActor. */
    public get authenticationInfo(): AuthenticationInfoPoco { return this.#info; }

    /** Gets the current bearer token. Undefined when there is no session. */
    public get token(): string|undefined { return this.#token; }

    /** Gets whether the session is remembered. */
    public get rememberMe(): boolean { return this.#rememberMe; }

    /**
     * Gets the actor if its current level is at least Normal, otherwise the anonymous.
     * Using an unsafe identity requires an explicit use of authenticationInfo.unsafeActor.
     */
    public get actor(): ActorInfoPoco { return AuthService.#safe( this.#info.unsafeActor, this.level ); }

    /** Gets the actual actor if its current level is at least Normal, otherwise the anonymous. */
    public get actualActor(): ActorInfoPoco { return AuthService.#safe( this.#info.unsafeActualActor, this.actualLevel ); }

    /** Gets the current level of the actor (the expirations are applied). */
    public get level(): AuthLevel { return this.#levelOf( this.#info.unsafeActor ); }

    /** Gets the current level of the actual actor (the expirations are applied). */
    public get actualLevel(): AuthLevel { return this.#levelOf( this.#info.unsafeActualActor ); }

    /** Gets whether the actor is not the actual actor. */
    public get isImpersonated(): boolean { return this.#info.unsafeActor.actorId !== this.#info.unsafeActualActor.actorId; }

    /**
     * Refreshes the session. The tabs refresh one at a time: a tab that waited for another one adopts its new token.
     */
    public refresh(): Promise<void> { return this.#refreshAsync( false ); }

    // When forced (after a remote login), the cached token is never adopted: the cookies have changed.
    async #refreshAsync( force: boolean ): Promise<void> {
        const before = this.#token;
        const run = async () => {
            const stored = this.#readStorage();
            if( !force && stored && stored.token !== before && this.#isAlive( stored.info ) ) {
                this.#adopt( stored );
                return;
            }
            await this.#endpoint.sendAsync( new RefreshSessionCommand() );
        };
        const locks = typeof navigator !== "undefined" ? navigator.locks : undefined;
        if( locks ) await locks.request( AuthService.refreshLockName, run );
        else await run();
    }

    /**
     * The name of the query parameter that the remote login callback adds to the return URL of an inline login:
     * "ok", "bound" (an identity has been bound to the current actor: see startInlineBinding) or the resource name of the error.
     */
    public static readonly remoteLoginMarker = "ck-auth-login";

    /** Gets or sets the features of the popup window of a remote login (see window.open). */
    public popupFeatures: string = "popup,width=500,height=650";

    /**
     * Gets the login schemes of the authentication center (see refreshSchemesAsync). A scheme whose criticalLifetime is
     * zero cannot elevate an actor to the Critical level.
     */
    public get availableSchemes(): ReadonlyArray<LoginSchemeInfo> { return this.#availableSchemes; }

    /**
     * Gets the schemes of the actual actor that can elevate it to the Critical level (see availableSchemes): the ones
     * to propose when a Critical level is required. Empty when the actual actor cannot reach the Critical level (or
     * is not authenticated, or the schemes have not been read).
     */
    public get criticalSchemes(): ReadonlyArray<string> {
        const elevating = new Set( this.#availableSchemes.filter( s => s.criticalLifetime.toMillis() > 0 ).map( s => s.name ) );
        return this.actualActor.schemes.map( s => s.scheme ).filter( s => elevating.has( s ) );
    }

    /**
     * Gets the error (a resource name like "Authentication.RemoteLoginRefused") of the last remote login, if it failed.
     */
    public get remoteLoginError(): string|undefined { return this.#remoteLoginError; }

    /**
     * Gets whether the last remote login was a binding that succeeded (see startInlineBinding and startPopupBinding):
     * the identity is bound to the current actor, the session is unchanged.
     */
    public get remoteLoginBound(): boolean { return this.#remoteLoginBound; }

    /**
     * Reads the login schemes of the authentication center: the remote ones are "Oidc.<Suffix>".
     * @returns The available schemes.
     */
    public async refreshSchemesAsync(): Promise<ReadonlyArray<LoginSchemeInfo>> {
        const r = await this.#endpoint.sendAsync( new GetLoginSchemesQCommand() );
        if( !(r.result instanceof CrisError) && r.result ) this.#availableSchemes = r.result;
        return this.#availableSchemes;
    }

    /**
     * Starts a remote login in the current window: the browser navigates to the provider and comes back to the
     * return URL (the session is then refreshed by the new page).
     * @param scheme The scheme ("Oidc.Google" for instance).
     * @param returnUrl The relative URL to return to. Defaults to the current path.
     * @param rememberMe Whether the session must be remembered.
     * @param userData Optional free data of the login (an invitation token for instance): kept by the authentication center
     *        until the end of the login, they are untrusted and limited (16 entries, 4096 characters).
     * @param critical True to reach the Critical level: the provider must authenticate the user again.
     * @returns The error if the login cannot start (the browser navigates otherwise).
     */
    public async startInlineLogin( scheme: string, returnUrl?: string, rememberMe: boolean = false, userData?: LoginUserData, critical: boolean = false ): Promise<CrisError|undefined> {
        const c = new StartLoginCommand();
        c.scheme = scheme;
        c.critical = critical;
        c.rememberMe = rememberMe;
        setLoginUserData( c.userData, userData );
        return this.#startInlineAsync( c, returnUrl );
    }

    /**
     * Binds the identity of a remote scheme to the current actor, in the current window: the browser navigates to the
     * provider and comes back to the return URL with the outcome (see remoteLoginBound and remoteLoginError). This
     * requires the Critical level (not impersonated); the identity must not be bound to another account; no account is
     * created and the session is unchanged.
     * @param scheme The scheme ("Oidc.Google" for instance).
     * @param returnUrl The relative URL to return to. Defaults to the current path.
     * @returns The error if the binding cannot start (the browser navigates otherwise).
     */
    public async startInlineBinding( scheme: string, returnUrl?: string ): Promise<CrisError|undefined> {
        const c = new StartLoginCommand();
        c.scheme = scheme;
        c.bindToCurrentActor = true;
        return this.#startInlineAsync( c, returnUrl );
    }

    async #startInlineAsync( c: StartLoginCommand, returnUrl: string|undefined ): Promise<CrisError|undefined> {
        c.inlineMode = true;
        c.returnUrl = returnUrl ?? (window.location.pathname + window.location.search);
        const r = await this.#endpoint.sendAsync( c );
        if( r.result instanceof CrisError ) return r.result;
        window.location.assign( (<RedirectedUrl>r.result).url );
        return undefined;
    }

    /**
     * Starts a remote login in a popup window. It must be called from a user gesture (a click): the popup is opened
     * before anything else, browsers block a window opened later.
     * The popup ends with a message from this origin (it carries no token): the session is then refreshed.
     * @param scheme The scheme ("Oidc.Google" for instance).
     * @param rememberMe Whether the session must be remembered.
     * @param userData Optional free data of the login (see startInlineLogin).
     * @param critical True to reach the Critical level: the provider must authenticate the user again.
     * @returns True on success, false otherwise (see remoteLoginError).
     */
    public async startPopupLogin( scheme: string, rememberMe: boolean = false, userData?: LoginUserData, critical: boolean = false ): Promise<boolean> {
        const popup = this.#openPopup();
        if( !popup ) return false;
        const c = new StartLoginCommand();
        c.scheme = scheme;
        c.rememberMe = rememberMe;
        c.critical = critical;
        setLoginUserData( c.userData, userData );
        return this.#startPopupAsync( popup, c );
    }

    /**
     * Binds the identity of a remote scheme to the current actor in a popup window (see startInlineBinding). It must be
     * called from a user gesture (a click). The session is unchanged: it is not refreshed (it keeps its Critical level).
     * @param scheme The scheme ("Oidc.Google" for instance).
     * @returns True on success (remoteLoginBound is true), false otherwise (see remoteLoginError).
     */
    public async startPopupBinding( scheme: string ): Promise<boolean> {
        const popup = this.#openPopup();
        if( !popup ) return false;
        const c = new StartLoginCommand();
        c.scheme = scheme;
        c.bindToCurrentActor = true;
        return this.#startPopupAsync( popup, c );
    }

    /**
     * Elevates the actor to the Critical level in a popup window that shows a login page of this application in its
     * critical mode: any scheme of the user that can elevate it can be used (see criticalSchemes), and the current page
     * keeps its state. It must be called from a user gesture (a click): the popup is opened before anything else.
     * The popup shares the session (the token is shared through the localStorage): this service sees the new level, and
     * then closes the popup. The popup stays on this origin (a remote login in it opens its own popup, see
     * startPopupLogin): a provider that isolates its pages from their opener cannot end the elevation.
     * @param loginUrl The URL of the login page in its critical mode (the page must not close itself).
     * @returns True when the level is Critical, false otherwise (see remoteLoginError: "Authentication.PopupBlocked" or
     *          "Authentication.PopupClosed").
     */
    public async startPopupElevation( loginUrl: string ): Promise<boolean> {
        if( this.level === AuthLevel.Critical ) return true;
        // Not the window of the remote logins: the login page of the popup can start one.
        const popup = this.#openPopup( "ck-auth-elevation" );
        if( !popup ) return false;
        popup.location.href = loginUrl;
        const success = await new Promise<boolean>( resolve => {
            let done = false;
            const end = ( s: boolean ) => {
                if( done ) return;
                done = true;
                this.removeOnChange( onChange );
                clearInterval( timer );
                resolve( s );
            };
            const onChange = () => { if( this.level === AuthLevel.Critical ) end( true ); };
            const timer = setInterval( () => { if( popup.closed ) end( this.level === AuthLevel.Critical ); }, 500 );
            this.addOnChange( onChange );
        } );
        if( !popup.closed ) popup.close();
        this.#remoteLoginError = success ? undefined : "Authentication.PopupClosed";
        return success;
    }

    #openPopup( name: string = "ck-auth-login" ): Window|null {
        this.#remoteLoginBound = false;
        const popup = window.open( "", name, this.popupFeatures );
        if( !popup ) this.#remoteLoginError = "Authentication.PopupBlocked";
        return popup;
    }

    async #startPopupAsync( popup: Window, c: StartLoginCommand ): Promise<boolean> {
        c.inlineMode = false;
        const r = await this.#endpoint.sendAsync( c );
        if( r.result instanceof CrisError || !r.result ) {
            popup.close();
            this.#remoteLoginError = r.result instanceof CrisError ? r.result.message : "Authentication.RemoteLoginFailed";
            return false;
        }
        popup.location.href = r.result.url;
        const success = await new Promise<boolean>( resolve => {
            let done = false;
            const end = ( s: boolean, error?: string ) => {
                if( done ) return;
                done = true;
                window.removeEventListener( "message", onMessage );
                clearInterval( timer );
                this.#remoteLoginError = s ? undefined : (error ?? "Authentication.RemoteLoginFailed");
                resolve( s );
            };
            // Only the callback page of this origin, in this popup, can end the login.
            const onMessage = ( e: MessageEvent ) => {
                if( e.origin !== window.location.origin || e.source !== popup || e.data?.type !== "ck-auth-remote-login" ) return;
                this.#remoteLoginBound = e.data.success === true && e.data.bound === true;
                end( e.data.success === true, e.data.error ?? undefined );
            };
            const timer = setInterval( () => { if( popup.closed ) end( false, "Authentication.PopupClosed" ); }, 500 );
            window.addEventListener( "message", onMessage );
        } );
        // A binding leaves the session unchanged: a refresh would lose its Critical level.
        if( success && !this.#remoteLoginBound ) await this.#refreshAsync( true );
        return success;
    }

    /** Ends the session. */
    public async logout(): Promise<void> {
        await this.#endpoint.sendAsync( new LogoutSessionCommand() );
    }

    /**
     * Impersonates another actor (this requires the Critical level and an impersonation service that allows it).
     * @param actor The actor name or identifier.
     * @returns The result of the session command.
     */
    public async impersonate( actor: string|number ): Promise<SessionResult|CrisError|undefined> {
        const c = new ImpersonateCommand();
        if( typeof actor === "number" ) c.targetActorId = actor;
        else c.targetActorName = actor;
        return (await this.#endpoint.sendAsync( c )).result;
    }

    /**
     * Ends the impersonation if any.
     * @returns The result of the session command.
     */
    public async clearImpersonation(): Promise<SessionResult|CrisError|undefined> {
        return (await this.#endpoint.sendAsync( new ClearImpersonationCommand() )).result;
    }

    /**
     * Registers a callback function that will be called when the authentication state changes.
     * @param func A callback function.
     */
    public addOnChange( func: ( eventSource: AuthService ) => void ): void {
        if( func ) this.#subscribers.add( func );
    }

    /**
     * Unregisters a previously registered callback.
     * @param func The callback function to remove.
     * @returns True if the callback has been found and removed, false otherwise.
     */
    public removeOnChange( func: ( eventSource: AuthService ) => void ): boolean {
        return this.#subscribers.delete( func );
    }

    /**
     * Closes this service: it no more follows the endpoint nor the other tabs. The session is not ended.
     */
    public close(): void {
        if( this.#closed ) return;
        this.#closed = true;
        this.#endpoint.removeHook( this.#hook );
        if( typeof window !== "undefined" ) {
            window.removeEventListener( "storage", this.#onStorage );
            window.removeEventListener( "online", this.#onWakeUp );
        }
        if( typeof document !== "undefined" ) document.removeEventListener( "visibilitychange", this.#onWakeUp );
        this.#clearTimer();
        this.#clearCriticalTimer();
        this.#subscribers.clear();
    }

    async #initializeAsync(): Promise<void> {
        // Back from an inline remote login: the cookies have changed, the cached token is stale.
        if( typeof window !== "undefined" ) {
            const url = new URL( window.location.href );
            const marker = url.searchParams.get( AuthService.remoteLoginMarker );
            if( marker !== null ) {
                url.searchParams.delete( AuthService.remoteLoginMarker );
                window.history.replaceState( window.history.state, "", url.pathname + url.search + url.hash );
                this.#remoteLoginBound = marker === "bound";
                this.#remoteLoginError = marker === "ok" || marker === "bound" ? undefined : marker;
                // A binding leaves the session unchanged: the cached token is still valid.
                if( !this.#remoteLoginBound ) {
                    await this.#refreshAsync( true );
                    return;
                }
            }
        }
        const stored = this.#readStorage();
        if( stored && this.#isAlive( stored.info ) ) {
            this.#apply( stored.info, stored.token, stored.rememberMe, false );
        }
        else {
            // No usable cached token: the refresh cookie may still be valid.
            await this.refresh();
        }
    }

    #onSending( command: ICommand<unknown> ): Record<string,string>|undefined {
        this.#sent.set( command, ++this.#sentCount );
        return this.#token ? { "Authorization": "Bearer " + this.#token } : undefined;
    }

    #onExecuted( e: ExecutedCommand<unknown> ): void {
        if( this.#closed ) return;
        const r = e.result;
        const isLogout = e.command instanceof LogoutSessionCommand;
        if( !isLogout && !(r instanceof SessionResult) && !(e.command instanceof RefreshSessionCommand) ) return;
        // The last sent session command wins: the answer of an older one (a refresh sent before a logout for instance)
        // that arrives late is ignored.
        const seq = this.#sent.get( e.command ) ?? 0;
        if( seq < this.#lastApplied ) return;
        let applied = true;
        if( isLogout ) {
            // The server clears the session whatever happens, unless it cannot be reached.
            if( !(r instanceof CrisError && r.errorType === "CommunicationError") ) this.#apply( undefined, undefined, false, true );
            else applied = false;
        }
        else if( r instanceof SessionResult ) {
            if( r.success && r.token ) this.#apply( r.info, r.token, r.rememberMe, true );
            // A failed refresh is a decision: it ends the session (the server cleared it). A failed login or impersonation
            // changes nothing.
            else if( e.command instanceof RefreshSessionCommand ) this.#apply( undefined, undefined, false, true );
            else applied = false;
        }
        else if( r instanceof CrisError && r.errorType === "ValidationError" ) {
            // A refresh refused by the validation: a decision too.
            this.#apply( undefined, undefined, false, true );
        }
        else {
            // A refresh that decided nothing (the server could not be reached, or failed): the session is kept and the
            // refresh is retried.
            applied = false;
            this.#scheduleRetry();
        }
        if( applied ) this.#lastApplied = seq;
    }

    #adopt( stored: StoredSession|undefined ): void {
        if( this.#closed ) return;
        if( stored && this.#isAlive( stored.info ) ) {
            if( stored.token !== this.#token ) this.#apply( stored.info, stored.token, stored.rememberMe, false );
        }
        else if( this.#token ) {
            this.#apply( undefined, undefined, false, false );
        }
    }

    #apply( info: AuthenticationInfoPoco|undefined, token: string|undefined, rememberMe: boolean, store: boolean ): void {
        // A state is applied: the retries (if any) are over.
        this.#retryDelay = 0;
        this.#info = info ?? new AuthenticationInfoPoco();
        this.#token = token;
        this.#rememberMe = rememberMe;
        if( store ) this.#writeStorage();
        this.#scheduleRefresh();
        this.#scheduleCriticalEnd();
        // The ambient values (the actor identifier...) depend on the authentication: the next commands wait for them.
        this.#endpoint.updateAmbientValuesAsync().catch( () => {} );
        this.#subscribers.forEach( func => func( this ) );
    }

    #scheduleRefresh(): void {
        this.#clearTimer();
        const exp = this.#token ? this.#info.expires?.toMillis() : undefined;
        if( exp === undefined ) return;
        // Refreshes at two thirds of the remaining time.
        const delay = Math.max( 1000, (exp - Date.now()) * 2 / 3 );
        this.#refreshTimer = setTimeout( () => { this.refresh().catch( e => console.error( "Session refresh failed.", e ) ); }, delay );
    }

    // Retries a refresh that decided nothing, with a backoff: 1 s, 2 s, 4 s... at most 30 s.
    #scheduleRetry(): void {
        if( this.#closed ) return;
        this.#clearTimer();
        this.#retryDelay = this.#retryDelay ? Math.min( this.#retryDelay * 2, AuthService.retryMaxDelay ) : AuthService.retryMinDelay;
        this.#refreshTimer = setTimeout( () => this.#retryNow(), this.#retryDelay );
    }

    #retryNow(): void {
        this.refresh().catch( e => console.error( "Session refresh failed.", e ) );
    }

    #clearTimer(): void {
        if( this.#refreshTimer !== undefined ) {
            clearTimeout( this.#refreshTimer );
            this.#refreshTimer = undefined;
        }
    }

    // The level is computed from the dates (see #levelOf): at the end of the Critical level, the subscribers are notified
    // so that the level they read is the new one.
    #scheduleCriticalEnd(): void {
        this.#clearCriticalTimer();
        const end = this.#info.criticalExpires?.toMillis();
        if( end === undefined || this.#closed ) return;
        const delay = end - Date.now();
        // setTimeout delays are 32-bit signed integers: a longer Critical level is not followed.
        if( delay <= 0 || delay > 0x7FFFFFFF ) return;
        this.#criticalTimer = setTimeout( () => {
            this.#criticalTimer = undefined;
            this.#subscribers.forEach( func => func( this ) );
        }, delay + 1 );
    }

    #clearCriticalTimer(): void {
        if( this.#criticalTimer !== undefined ) {
            clearTimeout( this.#criticalTimer );
            this.#criticalTimer = undefined;
        }
    }

    #isAlive( info: AuthenticationInfoPoco ): boolean {
        const exp = info.expires?.toMillis();
        return exp !== undefined && exp > Date.now();
    }

    #levelOf( a: ActorInfoPoco ): AuthLevel {
        let level = a.level;
        const now = Date.now();
        if( level === AuthLevel.Critical && !((this.#info.criticalExpires?.toMillis() ?? 0) > now) ) level = AuthLevel.Normal;
        if( level >= AuthLevel.Normal && !((this.#info.expires?.toMillis() ?? 0) > now) ) level = AuthLevel.Unsafe;
        return level;
    }

    static #safe( a: ActorInfoPoco, level: AuthLevel ): ActorInfoPoco {
        return level >= AuthLevel.Normal ? a : new ActorInfoPoco();
    }

    #readStorage(): StoredSession|undefined {
        try {
            const s = localStorage.getItem( this.#storageKey );
            if( !s ) return undefined;
            const r = CTSType.parse( s );
            return r instanceof SessionResult && r.token ? { info: r.info, token: r.token, rememberMe: r.rememberMe } : undefined;
        }
        catch {
            return undefined;
        }
    }

    #writeStorage(): void {
        try {
            if( this.#token ) {
                localStorage.setItem( this.#storageKey, CTSType.stringify( new SessionResult( this.#info, this.#token, this.#rememberMe ) ) );
            }
            else {
                localStorage.removeItem( this.#storageKey );
            }
        }
        catch( e ) {
            console.warn( "Unable to cache the authentication token.", e );
        }
    }
}

type StoredSession = { info: AuthenticationInfoPoco, token: string, rememberMe: boolean };
