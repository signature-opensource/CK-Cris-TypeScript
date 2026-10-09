import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faUserTag } from '@fortawesome/free-solid-svg-icons';
import { NotificationService, CrisError, HttpCrisEndpoint } from '@local/ck-gen';
import { WeakLoginCommand } from '@local/ck-gen/CK/IO/Authentication/Weak/WeakLoginCommand';

import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDividerModule } from 'ng-zorro-antd/divider';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';

@Component( {
  selector: 'ck-weak-login-form',
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    FontAwesomeModule,
    NzButtonModule,
    NzDividerModule,
    NzFormModule,
    NzInputModule
  ],
  templateUrl: './weak-login-form.html'
} )
export class WeakLoginForm {
  // <PreDependencyInjection revert />
  readonly #cris = inject( HttpCrisEndpoint );
  readonly #formBuilder = inject( FormBuilder );
  readonly #notifService = inject( NotificationService );
  readonly #translateService = inject( TranslateService );
  readonly #router = inject( Router );
  // <PostDependencyInjection />

  // <PreIconsDefinition revert />
  protected weakActorIcon = faUserTag;
  // <PostIconsDefinition />

  // <PreInputOutput revert />
  // A weak actor is not authenticated (Unsafe level): the private pages refuse it and the Login component does not
  // redirect it. On success, it is sent here: a public page (CK.Ng.PublicPage) or a route of the application.
  redirectTo = input<string>( '/' );
  // <PostInputOutput />

  // <PreLocalVariables revert />
  loginForm: FormGroup = this.#formBuilder.group( {
    weakActorName: new FormControl( '', { nonNullable: true, validators: [Validators.required] } )
  } );
  // <PostLocalVariables />

  async submit(): Promise<void> {
    if ( this.loginForm.valid ) {
      const command = new WeakLoginCommand();
      command.weakActorName = this.loginForm.get( 'weakActorName' )!.value.trim();
      // <PreSendCommand revert />
      // The AuthService follows the session commands sent through the endpoint.
      const r = ( await this.#cris.sendAsync( command ) ).result;
      if ( r instanceof CrisError ) {
        this.#notifService.displaySimpleMessage( 'error', r.message );
      }
      else if ( r?.success ) {
        this.#router.navigate( [this.redirectTo()] );
      }
      else {
        const m = r?.userMessages.find( m => m.message );
        this.#notifService.displaySimpleMessage( 'error', m ? m.message : this.#translateService.instant( 'CK.WeakLogin.Failure' ) );
      }
    }
  }

  async keyDown( event: KeyboardEvent ): Promise<void> {
    if ( event.code === "Enter" ) {
      await this.submit();
    }
  }
}
