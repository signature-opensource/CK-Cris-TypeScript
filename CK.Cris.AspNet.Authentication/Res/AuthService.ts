import { HttpCrisEndpoint, HttpCrisEndpointHook } from '@local/ck-gen/CK/Cris/HttpCrisEndpoint';
import { CrisError, ExecutedCommand } from '@local/ck-gen/CK/Cris/Model';
import { CTSType } from '@local/ck-gen/CK/Core/CTSType';
import { AuthLevel } from '@local/ck-gen/CK/Core/AuthLevel';
import { SessionResult } from '@local/ck-gen/CK/IO/Authentication/SessionResult';
import { AuthenticationInfoPoco } from '@local/ck-gen/CK/IO/Authentication/AuthenticationInfoPoco';
import { ActorInfoPoco } from '@local/ck-gen/CK/IO/Authentication/ActorInfoPoco';
import { RefreshSessionCommand } from '@local/ck-gen/CK/IO/Authentication/RefreshSessionCommand';
import { LogoutSessionCommand } from '@local/ck-gen/CK/IO/Authentication/LogoutSessionCommand';
import { ImpersonateCommand } from '@local/ck-gen/CK/IO/Authentication/ImpersonateCommand';
import { ClearImpersonationCommand } from '@local/ck-gen/CK/IO/Authentication/ClearImpersonationCommand';

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
    #isInitialized: Promise<void>;
    #closed: boolean;

    /**
     * The name of the lock (Web Locks API) that serializes the refreshes of the tabs.
     */
    public static readonly refreshLockName = "ck-auth-refresh";

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
        this.#subscribers = new Set();
        this.#hook = {
            getHeaders: () => this.#token ? { "Authorization": "Bearer " + this.#token } : undefined,
            onExecuted: e => this.#onExecuted( e )
        };
        this.#onStorage = e => {
            if( e.key === this.#storageKey ) this.#adopt( this.#readStorage() );
        };
        endpoint.addHook( this.#hook );
        if( typeof window !== "undefined" ) window.addEventListener( "storage", this.#onStorage );
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
    public async refresh(): Promise<void> {
        const before = this.#token;
        const run = async () => {
            const stored = this.#readStorage();
            if( stored && stored.token !== before && this.#isAlive( stored.info ) ) {
                this.#adopt( stored );
                return;
            }
            await this.#endpoint.sendAsync( new RefreshSessionCommand() );
        };
        const locks = typeof navigator !== "undefined" ? navigator.locks : undefined;
        if( locks ) await locks.request( AuthService.refreshLockName, run );
        else await run();
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
        if( typeof window !== "undefined" ) window.removeEventListener( "storage", this.#onStorage );
        this.#clearTimer();
        this.#subscribers.clear();
    }

    async #initializeAsync(): Promise<void> {
        const stored = this.#readStorage();
        if( stored && this.#isAlive( stored.info ) ) {
            this.#apply( stored.info, stored.token, stored.rememberMe, false );
        }
        else {
            // No usable cached token: the refresh cookie may still be valid.
            await this.refresh();
        }
    }

    #onExecuted( e: ExecutedCommand<unknown> ): void {
        if( this.#closed ) return;
        const r = e.result;
        if( e.command instanceof LogoutSessionCommand ) {
            // The server clears the session whatever happens, unless it cannot be reached.
            if( !(r instanceof CrisError && r.errorType === "CommunicationError") ) this.#apply( undefined, undefined, false, true );
        }
        else if( r instanceof SessionResult ) {
            if( r.success && r.token ) this.#apply( r.info, r.token, r.rememberMe, true );
            // A failed refresh ends the session (the server cleared it). A failed login or impersonation changes nothing.
            else if( e.command instanceof RefreshSessionCommand ) this.#apply( undefined, undefined, false, true );
        }
        else if( e.command instanceof RefreshSessionCommand
                 && r instanceof CrisError && r.errorType !== "CommunicationError" ) {
            this.#apply( undefined, undefined, false, true );
        }
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
        this.#info = info ?? new AuthenticationInfoPoco();
        this.#token = token;
        this.#rememberMe = rememberMe;
        if( store ) this.#writeStorage();
        this.#scheduleRefresh();
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

    #clearTimer(): void {
        if( this.#refreshTimer !== undefined ) {
            clearTimeout( this.#refreshTimer );
            this.#refreshTimer = undefined;
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
