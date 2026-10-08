import { Component, inject, computed, input } from '@angular/core';
import { Router } from '@angular/router';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faArrowRightFromBracket } from '@fortawesome/free-solid-svg-icons';
import { TranslateModule } from '@ngx-translate/core';
import { NzAvatarModule } from 'ng-zorro-antd/avatar';
import { NzDropDownModule } from 'ng-zorro-antd/dropdown';
import { NzDividerModule } from 'ng-zorro-antd/divider';
import { AuthLevel } from '@local/ck-gen/CK/Core/AuthLevel';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';

@Component( {
    selector: 'ck-user-info-box',
    imports: [FontAwesomeModule, TranslateModule, NzAvatarModule, NzDividerModule, NzDropDownModule],
    templateUrl: './user-info-box.html'
} )
export class UserInfoBox {
    // <PreDependencyInjection revert />
    readonly #authService = inject( NgAuthService );
    readonly #router = inject( Router );
    // <PostDependencyInjection />

    // <PreInputOutput revert />
    avatarSize = input<number>( 48 );
    // <PostInputOutput />

    // <PreIconsDefinition revert />
    protected signOutIcon = faArrowRightFromBracket;
    // <PostIconsDefinition />

    // <PreLocalVariables revert />
    actorName = computed( () => this.#authService.actor().actorName );
    // The actual actor is displayed when it is not the actor (impersonation).
    actualActorName = computed( () => this.#authService.isImpersonated() ? this.#authService.actualActor().actorName : '' );
    levelKey = computed( () => 'CK.UserInfoBox.Level.' + AuthLevel[this.#authService.level()] );

    actualAvatarSize = computed( () => {
        let result = this.avatarSize();
        // <AvatarSizeComputing />
        return result;
    } );

    avatarImgSrc = computed( () => {
        let result = '';
        // <AvatarImgSrcComputing />
        return result;
    } );

    avatarFallback = computed( () => {
        const trimAndUpper = ( s: string ) => s.trim().charAt( 0 ).toUpperCase();
        let result = this.actorName();
        // <PreAvatarFallbackComputing revert />
        // Split by separators and pick up to 2 initials
        const parts = result.split( /[\s._\-+~]+/ ).filter( Boolean );
        if ( parts.length >= 2 ) {
            return `${trimAndUpper( parts[0] )}${trimAndUpper( parts[1] )}`;
        }
        result = result.slice( 0, 2 ).toUpperCase();
        // <PostAvatarFallbackComputing />
        return result;
    } );
    // <PostLocalVariables />

    logout(): void {
        // <PreLogoutRedirection revert />
        this.#router.navigate( ['/auth/logout'] );
        // <PostLogoutRedirection />
    }
}
