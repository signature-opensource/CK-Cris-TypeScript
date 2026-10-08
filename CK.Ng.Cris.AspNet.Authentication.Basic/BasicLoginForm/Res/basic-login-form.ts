import { Component, inject } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
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
import { NzToolTipModule } from 'ng-zorro-antd/tooltip';

@Component( {
  selector: 'ck-basic-login-form',
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    TranslateModule,
    FontAwesomeModule,
    NzButtonModule,
    NzCheckboxModule,
    NzDividerModule,
    NzFormModule,
    NzInputModule,
    NzToolTipModule
  ],
    templateUrl: './basic-login-form.html'
} )
export class BasicLoginForm {
  // <PreDependencyInjection revert />
  readonly #authService = inject( AuthService );
  readonly #cris = inject( HttpCrisEndpoint );
  readonly #formBuilder = inject( FormBuilder );
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
  loginForm: FormGroup = this.#formBuilder.group( {
    userName: new FormControl( this.#authService.authenticationInfo.unsafeActor.actorName, { nonNullable: true, validators: [Validators.required] } ),
    password: new FormControl( '', { nonNullable: true, validators: [Validators.required] } ),
    rememberMe: new FormControl( this.#authService.rememberMe, { nonNullable: true } )
  } );
  showPassword: boolean = false;
  // <PostLocalVariables />

  async submit(): Promise<void> {
    if ( this.loginForm && this.loginForm.valid ) {
      const command = new BasicLoginCommand();
      command.userName = this.loginForm.get( 'userName' )!.value.trim();
      command.password = this.loginForm.get( 'password' )!.value;
      command.rememberMe = this.loginForm.get( 'rememberMe' )!.value;
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
