import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';

@Component( {
  selector: 'app-root',
  imports: [RouterOutlet, CKGenAppModule],
  templateUrl: './app.html',
  styleUrl: './app.less'
} )
export class AppComponent {
  readonly title = signal( 'CK_Ng_Cris_AspNet_Auth_Tests' );
}

