import { computed, inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService';
import { LoginSchemeInfo } from '@local/ck-gen/CK/IO/Authentication/LoginSchemeInfo';
import { AuthenticationInfoPoco } from '@local/ck-gen/CK/IO/Authentication/AuthenticationInfoPoco';
import { ActorInfoPoco } from '@local/ck-gen/CK/IO/Authentication/ActorInfoPoco';
import { AuthLevel } from '@local/ck-gen/CK/Core/AuthLevel';

/**
 * The Angular façade of the AuthService: the authentication state as signals.
 */
@Injectable( { providedIn: 'root' } )
export class NgAuthService {
    /**
     * Gets the AuthService.
     */
    readonly authService: AuthService = inject( AuthService );

    #authenticationInfo: WritableSignal<AuthenticationInfoPoco> = signal( this.authService.authenticationInfo );

    /**
     * Gets the authentication information, as it has been received (see actor and actualActor).
     */
    readonly authenticationInfo: Signal<AuthenticationInfoPoco> = this.#authenticationInfo.asReadonly();

    /**
     * Gets the actor if its level is at least Normal, otherwise the anonymous.
     */
    readonly actor: Signal<ActorInfoPoco> = computed( () => { this.#authenticationInfo(); return this.authService.actor; } );

    /**
     * Gets the actual actor if its level is at least Normal, otherwise the anonymous.
     */
    readonly actualActor: Signal<ActorInfoPoco> = computed( () => { this.#authenticationInfo(); return this.authService.actualActor; } );

    /**
     * Gets the level of the actor.
     */
    readonly level: Signal<AuthLevel> = computed( () => { this.#authenticationInfo(); return this.authService.level; } );

    /**
     * Gets whether the actor is not the actual actor.
     */
    readonly isImpersonated: Signal<boolean> = computed( () => { this.#authenticationInfo(); return this.authService.isImpersonated; } );

    readonly #router = inject( Router );
    #availableSchemes: WritableSignal<ReadonlyArray<LoginSchemeInfo>> = signal( [] );

    /**
     * Gets the login schemes of the authentication center (see refreshSchemesAsync).
     */
    readonly availableSchemes: Signal<ReadonlyArray<LoginSchemeInfo>> = this.#availableSchemes.asReadonly();

    /**
     * Gets the schemes of the actual actor that can elevate it to the Critical level (see AuthService.criticalSchemes).
     * The schemes must have been read (see refreshSchemesAsync).
     */
    readonly criticalSchemes: Signal<ReadonlyArray<string>> = computed( () => {
        this.#authenticationInfo();
        this.#availableSchemes();
        return this.authService.criticalSchemes;
    } );

    /**
     * Gets whether the actual actor can reach the Critical level (it has a login scheme that can elevate it): a user
     * interface can hide the actions that require the Critical level when it cannot. The schemes must have been read
     * (see refreshSchemesAsync).
     */
    readonly canReachCritical: Signal<boolean> = computed( () => this.criticalSchemes().length > 0 );

    /**
     * Reads the login schemes of the authentication center (see availableSchemes).
     * @returns The available schemes.
     */
    async refreshSchemesAsync(): Promise<ReadonlyArray<LoginSchemeInfo>> {
        const schemes = await this.authService.refreshSchemesAsync();
        this.#availableSchemes.set( schemes );
        return schemes;
    }

    /**
     * Navigates to the login page to reach the Critical level: it proposes only the schemes of the user that can elevate
     * it (see criticalSchemes) and comes back to the current page.
     * @param redirectTo The page to come back to. Defaults to the current one.
     */
    async navigateToCriticalLogin( redirectTo?: string ): Promise<boolean> {
        return this.#router.navigate( ['/auth'], { queryParams: { critical: true, redirectTo: redirectTo ?? this.#router.url } } );
    }

    constructor() {
        this.authService.addOnChange( ( eventSource: AuthService ) => this.#authenticationInfo.set( eventSource.authenticationInfo ) );
    }
}
