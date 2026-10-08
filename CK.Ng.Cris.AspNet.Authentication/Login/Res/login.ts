import { Component, effect, inject, OnInit } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { Router, ActivatedRoute } from '@angular/router';

import { NgAuthService } from '../NgAuthService';
import { ResponsiveDirective } from '@local/ck-gen';

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
    // <PostDependencyInjection />

    // assets can be overridden
    displayedLogoSrc: string = 'logos/login-logo.png';

    // <PreLocalVariables revert />
    // <PreRedirectionPath revert />
    redirectionPath: string = '';
    // <PostRedirectionPath />
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

    ngOnInit(): void {
        const redirect = this.#route.snapshot.queryParams['redirectTo'];
        if (redirect) {
            this.redirectionPath = redirect;
        }
        if (this.#authService.actor().actorId !== 0) {
            this.#router.navigate([this.redirectionPath]);
        } else if (this.redirectionPath !== '') {
            this.#location.replaceState(this.redirectionPath);
        }
    }
}
