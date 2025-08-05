import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app.component';
import { AuthService, BasicTestCommand, HttpCrisEndpoint, TestBatchCommand, TestCommand } from '@local/ck-gen';
import { CKGenAppModule } from '@local/ck-gen/CK/Angular/CKGenAppModule';

describe( 'CrisEndpoint', () => {
  let cris: HttpCrisEndpoint;
  let authService: AuthService;
  beforeEach( async () => {
    await TestBed.configureTestingModule( {
      imports: [AppComponent],
      providers: [...CKGenAppModule.Providers]
    } ).compileComponents();

    authService = TestBed.inject( AuthService );
    await authService.isInitialized;
    cris = TestBed.inject( HttpCrisEndpoint );
    await cris.updateAmbientValuesAsync();
  } );

  it( 'crisEndpoint should recursively set ambient values', async () => {
    await authService.basicLogin( 'Albert', 'success' );
    await cris.updateAmbientValuesAsync();
    const testBatchCommand = new TestBatchCommand();
    testBatchCommand.commands.push( { command: new BasicTestCommand(), description: 'Root batch command first item' } );
    // Dictionary<string, IAbstractCommand>
    testBatchCommand.test1.set( 'test1', new BasicTestCommand() );
    // List<IAbstractCommand>
    testBatchCommand.test2.push( new BasicTestCommand() );
    testBatchCommand.test2.push( new BasicTestCommand() );
    // IBasicTestCommand
    testBatchCommand.test3 = new BasicTestCommand();
    // ITestCommand (Also a ISimpleBatchCommand)
    const nested = new TestCommand();
    nested.commands.push( { command: new BasicTestCommand(), description: 'Nested batch command first item' } );
    testBatchCommand.test4 = nested;

    expect( cris.___a ).toBeDefined();

    testBatchCommand.commandModel.applyAmbientValues( testBatchCommand, cris );
    expect( testBatchCommand.actorId ).toBe( authService.authenticationInfo.user.userId );
    expect( ( testBatchCommand.commands[0].command as BasicTestCommand ).actorId ).toBe( testBatchCommand.actorId );
    expect( ( testBatchCommand.test1.get( 'test1' )! as BasicTestCommand ).actorId ).not.toBeNull();
    expect( ( testBatchCommand.test1.get( 'test1' )! as BasicTestCommand ).actorId ).toBe( testBatchCommand.actorId );
    expect( ( testBatchCommand.test2[0] as BasicTestCommand ).actorId ).toBe( testBatchCommand.actorId );
    expect( ( testBatchCommand.test2[1] as BasicTestCommand ).actorId ).toBe( testBatchCommand.actorId );
    expect( ( testBatchCommand.test3 as BasicTestCommand ).actorId ).toBe( testBatchCommand.actorId );
    expect( testBatchCommand.test4.actorId ).toBe( testBatchCommand.actorId );
    expect( ( testBatchCommand.test4.commands[0].command as BasicTestCommand ).actorId ).toBe( testBatchCommand.actorId );
  } );
} );
