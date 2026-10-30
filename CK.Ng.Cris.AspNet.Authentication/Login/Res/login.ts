import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Router, ActivatedRoute } from '@angular/router';

import { NgAuthService } from '../NgAuthService';
import { AuthLevel } from '@local/ck-gen/CK/Core/AuthLevel';
import { NotificationService, ResponsiveDirective } from '@local/ck-gen';

import { NzButtonModule } from 'ng-zorro-antd/button';

@Component({
    selector: 'ck-login',
    imports: [
        CommonModule,
        TranslateModule,
        ResponsiveDirective,
        NzButtonModule
    ],
    templateUrl: './login.html'
})
export class Login implements OnInit {
    // <PreDependencyInjection revert />
    readonly #authService = inject(NgAuthService);
    readonly #router = inject(Router);
    readonly #route = inject(ActivatedRoute);
    readonly #location = inject(Location);
    readonly #notifService = inject(NotificationService);
    readonly #translateService = inject(TranslateService);
    // <PostDependencyInjection />

    // assets can be overridden
    displayedLogoSrc: string = 'logos/login-logo.png';

    // <PreLocalVariables revert />
    // <PreRedirectionPath revert />
    redirectionPath: string = '';
    // <PostRedirectionPath />
    // The remote logins happen in a popup by default (the application keeps its state).
    remoteLoginInline: boolean = false;
    // The remote schemes ("Oidc.<Suffix>"): the last used first.
    remoteSchemes = signal<ReadonlyArray<string>>( [] );
    // "/auth?critical": an authenticated user logs in again to reach the Critical level. Only its schemes that can
    // elevate it are proposed (see NgAuthService.criticalSchemes) and a remote provider must authenticate it again.
    readonly critical = signal( this.#route.snapshot.queryParamMap.has( 'critical' ) && this.#authService.actualActor().actorId !== 0 );
    // "/auth?critical&popup": the popup of an elevation (see NgAuthService.elevateToCritical). Once the level is Critical,
    // its opener sees it (the session is shared) and closes it: it doesn't redirect.
    readonly popup = this.critical() && this.#route.snapshot.queryParamMap.has( 'popup' ) && !!window.opener;
    // <PostLocalVariables />

    constructor() {
        // A login is a session command sent through the HttpCrisEndpoint: whoever sends it,
        // the actor changes (the level reaches Critical in the critical mode) and the user is redirected.
        effect(() => {
            if (this.critical() ? this.#authService.level() === AuthLevel.Critical : this.#authService.actor().actorId !== 0) {
                this.#redirect();
            }
        });
    }

    /**
     * Gets whether a login method is proposed: all of them, except in the critical mode where only the schemes of the
     * user that can elevate it are.
     * @param scheme The scheme ("Basic", "Passkey", "Oidc.Google", etc.).
     */
    canShow(scheme: string): boolean {
        return !this.critical() || this.#authService.criticalSchemes().includes(scheme);
    }

    async ngOnInit(): Promise<void> {
        const redirect = this.#route.snapshot.queryParams['redirectTo'];
        if (redirect) {
            this.redirectionPath = redirect;
        }
        if (this.critical() ? this.#authService.level() === AuthLevel.Critical : this.#authService.actor().actorId !== 0) {
            this.#redirect();
            return;
        } else if (this.redirectionPath !== '' && !this.critical()) {
            this.#location.replaceState(this.redirectionPath);
        }
        // Back from an inline remote login that failed.
        this.#notifyError(this.#authService.authService.remoteLoginError);
        const schemes = (await this.#authService.refreshSchemesAsync()).map(s => s.name);
        this.remoteSchemes.set(this.#sortByLastUsed(schemes.filter(s => s.startsWith('Oidc.') && this.canShow(s))));
    }

    async loginWithProvider(p: string): Promise<void> {
        const auth = this.#authService.authService;
        if (this.remoteLoginInline) {
            const error = await auth.startInlineLogin(p, this.redirectionPath || '/', false, undefined, this.critical());
            if (error) this.#notifyError(error.message);
        }
        else if (!await auth.startPopupLogin(p, false, undefined, this.critical()) && auth.remoteLoginError !== 'Authentication.PopupClosed') {
            this.#notifyError(auth.remoteLoginError);
        }
    }

    getProviderLogoSrc(p: string): string {
        return `login-providers/${this.getProviderName(p).toLocaleLowerCase()}.png`;
    }

    getProviderName(p: string): string {
        return p.startsWith('Oidc.') ? p.substring(5) : p;
    }

    #redirect(): void {
        // The popup of an elevation is closed by its opener (it redirects only when its opener is gone).
        if (this.popup && window.opener && !window.opener.closed) return;
        this.#router.navigate([this.redirectionPath]);
    }

    #notifyError(error: string | undefined): void {
        if (error) {
            const text = this.#translateService.instant(error);
            this.#notifService.displaySimpleMessage('error', text !== error ? text : this.#translateService.instant('Login.RemoteLoginFailed'));
        }
    }

    #sortByLastUsed(schemes: Array<string>): Array<string> {
        const lastUsed = new Map<string, number>();
        this.#authService.authenticationInfo().unsafeActor.schemes.forEach(s => lastUsed.set(s.scheme, s.lastUsed.toMillis()));
        return [...schemes].sort((a, b) => (lastUsed.get(b) ?? 0) - (lastUsed.get(a) ?? 0));
    }
}
