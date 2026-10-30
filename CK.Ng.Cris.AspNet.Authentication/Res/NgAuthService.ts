import { computed, inject, Injectable, Signal, signal, WritableSignal } from '@angular/core';
import { Location } from '@angular/common';
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

    // Each change of the AuthService is a change, even with the same information: the level depends on the time (the end
    // of the Critical level).
    #authenticationInfo: WritableSignal<AuthenticationInfoPoco> = signal( this.authService.authenticationInfo, { equal: () => false } );

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
    readonly #location = inject( Location );
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
     * Elevates the actor to the Critical level with the login page in its critical mode ("/auth?critical"): it proposes
     * only the schemes of the user that can elevate it (see criticalSchemes).
     * <p>
     * By default, the login page is in a popup (see AuthService.startPopupElevation): the current page keeps its state
     * (a form being filled, the selected tab). It must be called from a user gesture (a click): the popup is opened
     * before anything else. The inline mode navigates to the login page, that comes back to the current page.
     * </p>
     * @param inlineMode True to navigate to the login page in the current window instead of a popup.
     * @returns The popup: true when the level is Critical, false otherwise (see AuthService.remoteLoginError:
     *          "Authentication.PopupBlocked" or "Authentication.PopupClosed"). The inline mode: whether the navigation
     *          has started.
     */
    async elevateToCritical( inlineMode: boolean = false ): Promise<boolean> {
        if( inlineMode ) {
            return this.#router.navigate( ['/auth'], { queryParams: { critical: true, redirectTo: this.#router.url } } );
        }
        const url = this.#router.serializeUrl( this.#router.createUrlTree( ['/auth'], { queryParams: { critical: true, popup: true } } ) );
        return this.authService.startPopupElevation( this.#location.prepareExternalUrl( url ) );
    }

    constructor() {
        this.authService.addOnChange( ( eventSource: AuthService ) => this.#authenticationInfo.set( eventSource.authenticationInfo ) );
    }
}
