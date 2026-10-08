import { computed, inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { AuthService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService';
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

    constructor() {
        this.authService.addOnChange( ( eventSource: AuthService ) => this.#authenticationInfo.set( eventSource.authenticationInfo ) );
    }
}
