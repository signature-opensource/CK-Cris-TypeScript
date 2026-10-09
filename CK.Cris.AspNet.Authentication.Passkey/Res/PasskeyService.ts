import { HttpCrisEndpoint } from '@local/ck-gen/CK/Cris/HttpCrisEndpoint';
import { CrisError } from '@local/ck-gen/CK/Cris/Model';
import { SessionResult } from '@local/ck-gen/CK/IO/Authentication/SessionResult';
import { StartPasskeyLoginCommand } from '@local/ck-gen/CK/IO/Authentication/Passkey/StartPasskeyLoginCommand';
import { PasskeyLoginCommand } from '@local/ck-gen/CK/IO/Authentication/Passkey/PasskeyLoginCommand';
import { StartPasskeyRegistrationCommand } from '@local/ck-gen/CK/IO/Authentication/Passkey/StartPasskeyRegistrationCommand';
import { PasskeyRegistrationCommand } from '@local/ck-gen/CK/IO/Authentication/Passkey/PasskeyRegistrationCommand';
import { PasskeyRegistrationResult } from '@local/ck-gen/CK/IO/Authentication/Passkey/PasskeyRegistrationResult';
import { LoginUserData, setLoginUserData } from '@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService';

/**
 * The outcome of a passkey ceremony.
 */
export interface PasskeyOutcome<T> {
    /** The result of the server (absent when the ceremony stopped before). */
    readonly result?: T;
    /** The error (a message or a resource name like "Authentication.PasskeyVerificationFailed"); absent on success. */
    readonly error?: string;
    /** True when the user cancelled (or the authenticator timed out): this is not an error to display. */
    readonly cancelled: boolean;
}

// The JSON helpers of WebAuthn Level 3 (PublicKeyCredential.parse...FromJSON, credential.toJSON()).
interface WebAuthnJson {
    parseRequestOptionsFromJSON( options: unknown ): PublicKeyCredentialRequestOptions;
    parseCreationOptionsFromJSON( options: unknown ): PublicKeyCredentialCreationOptions;
}

/**
 * The passkey (WebAuthn) ceremonies of the browser, over the Cris endpoint: a usernameless login and the registration
 * of a new passkey for the current actor.
 * <p>
 * Each ceremony is a start command (the server returns the WebAuthn options as JSON), the browser's call
 * (navigator.credentials.get or create: the user verifies with a PIN or biometrics), and a finish command with the
 * browser's response. The login is a session command sent through the endpoint: the AuthService follows it.
 * </p>
 */
export class PasskeyService {
    readonly #endpoint: HttpCrisEndpoint;

    constructor( endpoint: HttpCrisEndpoint ) {
        this.#endpoint = endpoint;
    }

    /**
     * Gets whether this browser supports passkeys with the WebAuthn JSON helpers (recent browsers only: no polyfill).
     */
    public static get isSupported(): boolean {
        const p = ( globalThis as any ).PublicKeyCredential;
        return typeof p === 'function'
               && typeof p.parseRequestOptionsFromJSON === 'function'
               && typeof p.parseCreationOptionsFromJSON === 'function'
               && typeof navigator !== 'undefined'
               && !!navigator.credentials;
    }

    /**
     * Logs in with a passkey: the browser proposes the passkeys it holds for this site (no user name).
     * It should be called from a user gesture (a click).
     * @param rememberMe Whether the session must be remembered.
     * @param userData Optional free data of the login (an invitation token for instance).
     * @returns The outcome: its result is the session result.
     */
    public async loginAsync( rememberMe: boolean = false, userData?: LoginUserData ): Promise<PasskeyOutcome<SessionResult>> {
        if( !PasskeyService.isSupported ) return { error: 'Authentication.PasskeyNotSupported', cancelled: false };
        const start = await this.#endpoint.sendAsync( new StartPasskeyLoginCommand() );
        if( start.result instanceof CrisError ) return { error: start.result.message, cancelled: false };
        const options = PasskeyService.#json.parseRequestOptionsFromJSON( JSON.parse( start.result ) );
        let credential: Credential | null;
        try {
            credential = await navigator.credentials.get( { publicKey: options } );
        }
        catch( e ) {
            return PasskeyService.#browserFailure( e );
        }
        if( !credential ) return { cancelled: true };
        const c = new PasskeyLoginCommand();
        c.response = JSON.stringify( ( credential as any ).toJSON() );
        c.rememberMe = rememberMe;
        setLoginUserData( c.userData, userData );
        const r = await this.#endpoint.sendAsync( c );
        if( r.result instanceof CrisError ) return { error: r.result.message, cancelled: false };
        if( !r.result.success ) return { result: r.result, error: PasskeyService.#firstError( r.result.userMessages, 'Authentication.PasskeyLoginFailed' ), cancelled: false };
        return { result: r.result, cancelled: false };
    }

    /**
     * Registers a new passkey for the current actor. The session must be at the Critical level (a recent login) and
     * not impersonated. It should be called from a user gesture (a click).
     * @param name The name of the passkey ("My phone"). A default name is given when undefined.
     * @returns The outcome: its result has the name of the registered passkey.
     */
    public async registerAsync( name?: string ): Promise<PasskeyOutcome<PasskeyRegistrationResult>> {
        if( !PasskeyService.isSupported ) return { error: 'Authentication.PasskeyNotSupported', cancelled: false };
        const s = new StartPasskeyRegistrationCommand();
        if( name ) s.name = name;
        const start = await this.#endpoint.sendAsync( s );
        if( start.result instanceof CrisError ) return { error: start.result.message, cancelled: false };
        const options = PasskeyService.#json.parseCreationOptionsFromJSON( JSON.parse( start.result ) );
        let credential: Credential | null;
        try {
            credential = await navigator.credentials.create( { publicKey: options } );
        }
        catch( e ) {
            return PasskeyService.#browserFailure( e );
        }
        if( !credential ) return { cancelled: true };
        const c = new PasskeyRegistrationCommand();
        c.response = JSON.stringify( ( credential as any ).toJSON() );
        const r = await this.#endpoint.sendAsync( c );
        if( r.result instanceof CrisError ) return { error: r.result.message, cancelled: false };
        if( !r.result.success ) return { result: r.result, error: PasskeyService.#firstError( r.result.userMessages, 'Authentication.PasskeyRegistrationFailed' ), cancelled: false };
        return { result: r.result, cancelled: false };
    }

    static get #json(): WebAuthnJson {
        return ( globalThis as any ).PublicKeyCredential as WebAuthnJson;
    }

    // NotAllowedError and AbortError: the user cancelled, or the authenticator timed out (WebAuthn doesn't tell them
    // apart, on purpose). InvalidStateError at registration: this authenticator already holds a passkey of the actor.
    static #browserFailure<T>( e: unknown ): PasskeyOutcome<T> {
        const name = e instanceof DOMException ? e.name : undefined;
        if( name === 'NotAllowedError' || name === 'AbortError' ) return { cancelled: true };
        if( name === 'InvalidStateError' ) return { error: 'Authentication.PasskeyExists', cancelled: false };
        return { error: e instanceof Error ? e.message : String( e ), cancelled: false };
    }

    static #firstError( messages: ReadonlyArray<{ message: string, resName?: string }>, defaultError: string ): string {
        const m = messages.find( m => m.message );
        return m ? m.message : defaultError;
    }
}
