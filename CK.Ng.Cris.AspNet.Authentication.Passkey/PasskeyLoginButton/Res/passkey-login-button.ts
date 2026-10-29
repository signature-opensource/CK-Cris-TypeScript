import { Component, inject, input, OnInit, signal } from '@angular/core';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faKey } from '@fortawesome/free-solid-svg-icons';
import { NotificationService, HttpCrisEndpoint } from '@local/ck-gen';
import { PasskeyService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/Passkey/PasskeyService';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';

import { NzButtonModule } from 'ng-zorro-antd/button';

@Component( {
  selector: 'ck-passkey-login-button',
  imports: [
    TranslateModule,
    FontAwesomeModule,
    NzButtonModule
  ],
  templateUrl: './passkey-login-button.html'
} )
export class PasskeyLoginButton implements OnInit {
  // <PreDependencyInjection revert />
  readonly #passkeys = new PasskeyService( inject( HttpCrisEndpoint ) );
  readonly #auth = inject( NgAuthService );
  readonly #notifService = inject( NotificationService );
  readonly #translateService = inject( TranslateService );
  // <PostDependencyInjection />

  // <PreIconsDefinition revert />
  protected passkeyIcon = faKey;
  // <PostIconsDefinition />

  // <PreInputOutput revert />
  rememberMe = input<boolean>( false );
  // <PostInputOutput />

  // <PreLocalVariables revert />
  // Shown only when the authentication center offers the "Passkey" scheme and the browser supports passkeys.
  protected available = signal( false );
  protected busy = signal( false );
  // <PostLocalVariables />

  async ngOnInit(): Promise<void> {
    if( PasskeyService.isSupported ) {
      const schemes = await this.#auth.refreshSchemesAsync();
      this.available.set( schemes.some( s => s.name === 'Passkey' ) );
    }
  }

  // The login page follows the session: on success, the actor changes and it redirects.
  async login(): Promise<void> {
    this.busy.set( true );
    try {
      const outcome = await this.#passkeys.loginAsync( this.rememberMe() );
      if( outcome.error ) {
        this.#notifService.displaySimpleMessage( 'error', outcome.error.startsWith( 'Authentication.' )
                                                            ? this.#translateService.instant( 'CK.PasskeyLogin.Failure' )
                                                            : outcome.error );
      }
    }
    finally {
      this.busy.set( false );
    }
  }
}
