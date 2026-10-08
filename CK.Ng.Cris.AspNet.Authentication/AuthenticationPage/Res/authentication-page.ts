import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faArrowLeft } from '@fortawesome/free-solid-svg-icons';

import { NzButtonModule } from 'ng-zorro-antd/button';
import { ResponsiveDirective } from '@local/ck-gen';

@Component( {
    selector: 'ck-authentication-page',
    imports: [
        CommonModule,
        RouterOutlet,
        RouterLink,
        TranslateModule,
        ResponsiveDirective,
        FontAwesomeModule,
        NzButtonModule,
    ],
    templateUrl: './authentication-page.html'
} )
export class AuthenticationPage {
    // <PreDependencyInjection revert />
    readonly #route = inject(ActivatedRoute);
    // <PostDependencyInjection />

    // <PreIconsDefinition revert />
    protected leftIcon = faArrowLeft;
    // <PostIconsDefinition />

    // <PreLocalVariables revert />
    // assets can be overridden
    displayedLogoSrc: string = 'logos/login-logo.png';
    showReturnButton: boolean = false;
    // <PostLocalVariables />

    // TODO: handle light/dark mode toggle
    //logoWhiteSrc = input<string>( 'logos/login-logo-white.png' );
    constructor() {
        const redirect = this.#route.snapshot.queryParams['redirectTo'];
        if (!redirect) {
            this.showReturnButton = true;
        }
    }
}
