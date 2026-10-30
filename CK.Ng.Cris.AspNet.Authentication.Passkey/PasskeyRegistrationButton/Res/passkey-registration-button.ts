import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faKey } from '@fortawesome/free-solid-svg-icons';
import { NotificationService, HttpCrisEndpoint } from '@local/ck-gen';
import { AuthLevel } from '@local/ck-gen/CK/Core/AuthLevel';
import { PasskeyService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/Passkey/PasskeyService';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';

import { NzButtonModule } from 'ng-zorro-antd/button';

@Component( {
  selector: 'ck-passkey-registration-button',
  imports: [
    TranslateModule,
    FontAwesomeModule,
    NzButtonModule
  ],
  templateUrl: './passkey-registration-button.html'
} )
export class PasskeyRegistrationButton implements OnInit {
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
  // The name of the new passkey ("My phone"): a default name is given when empty.
  passkeyName = input<string>( '' );
  // Raised with the name of the registered passkey.
  registered = output<string>();
  // <PostInputOutput />

  // <PreLocalVariables revert />
  protected busy = signal( false );
  // A passkey can be added from a recent login only (Critical level), never while impersonating.
  protected canRegister = computed( () => PasskeyService.isSupported
                                          && this.#auth.level() === AuthLevel.Critical
                                          && !this.#auth.isImpersonated() );
  // Not at the Critical level: a new login with one of the user's schemes that can elevate it is required, when there is one.
  protected needsCriticalLogin = computed( () => PasskeyService.isSupported
                                                 && this.#auth.level() !== AuthLevel.Critical
                                                 && !this.#auth.isImpersonated() );
  protected canReachCritical = this.#auth.canReachCritical;
  // <PostLocalVariables />

  async ngOnInit(): Promise<void> {
    // The schemes tell whether the user can reach the Critical level.
    if( this.#auth.availableSchemes().length === 0 ) await this.#auth.refreshSchemesAsync();
  }

  // A login in a popup: the page keeps its state (the passkey name being typed).
  async logInAgain(): Promise<void> {
    const auth = this.#auth.authService;
    if( !await this.#auth.elevateToCritical() && auth.remoteLoginError !== 'Authentication.PopupClosed' ) {
      this.#notifService.displaySimpleMessage( 'error', this.#translateService.instant( auth.remoteLoginError ?? 'Login.RemoteLoginFailed' ) );
    }
  }

  async register(): Promise<void> {
    this.busy.set( true );
    try {
      const outcome = await this.#passkeys.registerAsync( this.passkeyName() || undefined );
      if( outcome.cancelled ) return;
      if( outcome.error ) {
        this.#notifService.displaySimpleMessage( 'error', outcome.error === 'Authentication.PasskeyExists'
                                                            ? this.#translateService.instant( 'CK.PasskeyRegistration.Exists' )
                                                            : outcome.error.startsWith( 'Authentication.' )
                                                                ? this.#translateService.instant( 'CK.PasskeyRegistration.Failure' )
                                                                : outcome.error );
        return;
      }
      const name = outcome.result!.name;
      this.#notifService.displaySimpleMessage( 'success', this.#translateService.instant( 'CK.PasskeyRegistration.Success', { name } ) );
      this.registered.emit( name );
    }
    finally {
      this.busy.set( false );
    }
  }
}
