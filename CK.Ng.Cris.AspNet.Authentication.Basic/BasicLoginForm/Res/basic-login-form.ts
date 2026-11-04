import { Component, inject, signal } from '@angular/core';
import { form, FormField, FormRoot, required } from '@angular/forms/signals';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faEye, faEyeSlash, faLock, faUser } from '@fortawesome/free-solid-svg-icons';
import { NotificationService, CrisError, HttpCrisEndpoint } from '@local/ck-gen';
import { AuthService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService';
import { BasicLoginCommand } from '@local/ck-gen/CK/IO/Authentication/Basic/BasicLoginCommand';

import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzDividerModule } from 'ng-zorro-antd/divider';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';

@Component( {
  selector: 'ck-basic-login-form',
  imports: [
    CommonModule,
    FormField,
    FormRoot,
    TranslatePipe,
    FontAwesomeModule,
    NzButtonModule,
    NzCheckboxModule,
    NzDividerModule,
    NzFormModule,
    NzInputModule,
    NzTooltipModule
  ],
    templateUrl: './basic-login-form.html'
} )
export class BasicLoginForm {
  // <PreDependencyInjection revert />
  readonly #authService = inject( AuthService );
  readonly #cris = inject( HttpCrisEndpoint );
  readonly #notifService = inject( NotificationService );
  readonly #translateService = inject( TranslateService );
  // <PostDependencyInjection />

  // <PreIconsDefinition revert />
  protected eyeIcon = faEye;
  protected eyeSlashIcon = faEyeSlash;
  protected passwordIcon = faLock;
  protected userIcon = faUser;
  // <PostIconsDefinition />

  // <PreLocalVariables revert />
  readonly loginModel = signal( {
    userName: this.#authService.authenticationInfo.unsafeActor.actorName,
    password: '',
    rememberMe: this.#authService.rememberMe
  } );
  readonly loginForm = form( this.loginModel, p => {
    required( p.userName );
    required( p.password );
  }, { name: 'login' } );
  showPassword: boolean = false;
  // <PostLocalVariables />

  async submit(): Promise<void> {
    if ( this.loginForm().valid() ) {
      const { userName, password, rememberMe } = this.loginModel();
      const command = new BasicLoginCommand();
      command.userName = userName.trim();
      command.password = password;
      command.rememberMe = rememberMe;
      // The AuthService follows the session commands sent through the endpoint.
      const r = ( await this.#cris.sendAsync( command ) ).result;
      if ( r instanceof CrisError ) {
        this.#notifService.displaySimpleMessage( 'error', r.message );
      }
      else if ( !r?.success ) {
        const m = r?.userMessages.find( m => m.message );
        this.#notifService.displaySimpleMessage( 'error', m ? m.message : this.#translateService.instant( 'User.LoginFailure' ) );
      }
    }
  }

  async keyDown( event: KeyboardEvent ): Promise<void> {
    if ( event.code === "Enter" ) {
      await this.submit();
    }
  }
}
