import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Router, ActivatedRoute } from '@angular/router';

import { NgAuthService } from '../NgAuthService';
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
    // <PostLocalVariables />

    constructor() {
        // A login is a session command sent through the HttpCrisEndpoint: whoever sends it,
        // the actor changes and the user is redirected.
        effect(() => {
            if (this.#authService.actor().actorId !== 0) {
                this.#router.navigate([this.redirectionPath]);
            }
        });
    }

    async ngOnInit(): Promise<void> {
        const redirect = this.#route.snapshot.queryParams['redirectTo'];
        if (redirect) {
            this.redirectionPath = redirect;
        }
        if (this.#authService.actor().actorId !== 0) {
            this.#router.navigate([this.redirectionPath]);
            return;
        } else if (this.redirectionPath !== '') {
            this.#location.replaceState(this.redirectionPath);
        }
        // Back from an inline remote login that failed.
        this.#notifyError(this.#authService.authService.remoteLoginError);
        const schemes = await this.#authService.authService.refreshSchemesAsync();
        this.remoteSchemes.set(this.#sortByLastUsed(schemes.filter(s => s.startsWith('Oidc.'))));
    }

    async loginWithProvider(p: string): Promise<void> {
        const auth = this.#authService.authService;
        if (this.remoteLoginInline) {
            const error = await auth.startInlineLogin(p, this.redirectionPath || '/');
            if (error) this.#notifyError(error.message);
        }
        else if (!await auth.startPopupLogin(p) && auth.remoteLoginError !== 'Authentication.PopupClosed') {
            this.#notifyError(auth.remoteLoginError);
        }
    }

    getProviderLogoSrc(p: string): string {
        return `login-providers/${this.getProviderName(p).toLocaleLowerCase()}.png`;
    }

    getProviderName(p: string): string {
        return p.startsWith('Oidc.') ? p.substring(5) : p;
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
