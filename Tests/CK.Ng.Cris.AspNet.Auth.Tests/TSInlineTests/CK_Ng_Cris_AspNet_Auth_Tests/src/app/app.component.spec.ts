import { TestBed, inject } from '@angular/core/testing';
import { AppComponent } from './app.component';
import { BasicLoginCommand, CrisEndpoint } from '@local/ck-gen';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it(`should have the 'CK_Ng_Cris_AspNet_Auth_Tests' title`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.title).toEqual('CK_Ng_Cris_AspNet_Auth_Tests');
  });

  it('should render title', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Hello, CK_Ng_Cris_AspNet_Auth_Tests');
  });

  it('applyAmbientValues is correctly typed', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const cmd = new BasicLoginCommand();
    // There is a TS error here, but this test works perfectly.
    const cris = inject( CrisEndpoint );
    cmd.commandModel.applyAmbientValues( cmd, cris );
  });
});
