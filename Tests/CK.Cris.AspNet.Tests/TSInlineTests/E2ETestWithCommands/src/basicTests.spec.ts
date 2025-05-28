import axios from "axios"; 
import { HttpCrisEndpoint, CrisError, UserMessageLevel, SimpleUserMessage } from "@local/ck-gen"; 
import { BeautifulCommand, BuggyCommand, WithMessageCommand, WithUserMessageCommand } from "@local/ck-gen"; 

// Trick from https://stackoverflow.com/a/77047461/190380
if( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout(20 * 60 * 1000 ); // 20 minutes

const crisEndpoint = CKTypeScriptEnv["CRIS_ENDPOINT_URL"] ?? "";
const withEndpoint = crisEndpoint ? it : it.skip;

it('isConnect is false until the first command is sent', async () => {
  const ep = new HttpCrisEndpoint( axios, crisEndpoint );
  expect( ep.isConnected ).toBeFalsy();
  await ep.sendAsync( new BeautifulCommand( "Gorgeous" ) );
  expect( ep.isConnected ).toBeTruthy();
});

it( 'default endpoint color is red.', async () =>
{
  const ep = new HttpCrisEndpoint( axios, crisEndpoint );
  // Endpoint values are not exposed since they are refreshable: 
  // hiding them avoids a potential use of stale data.
  // An explicit update is required.
  const a = await ep.updateAmbientValuesAsync();
  expect( a.color ).toBe('Red');
});

it( 'endpoint values are set on sent command.', async () => 
{
  const ep = new HttpCrisEndpoint( axios, crisEndpoint );
  const cmd = new BeautifulCommand("Gorgeous");
  // color is and endpoint value: it is automatically set
  // when sending the command if it not set before. 
  expect( cmd.color ).toBeUndefined();
  const a = await ep.updateAmbientValuesAsync();
  // The BeatifulCommand handler returns a string that is "Color - Beauty".
  const executedCommand = await ep.sendAsync( cmd );
  expect( cmd.color ).toBe( "Red" );
  expect( executedCommand.result ).toBe( "Red - Gorgeous" );
}, 900000000 );

it( 'endpoint values can be overridden.', async () => 
{
  const ep = new HttpCrisEndpoint( axios, crisEndpoint );
  const cmd = new BeautifulCommand( "Superb" );
  ep.ambientValuesOverride.color = "Black";
  // color is and endpoint value: it is automatically set
  // when sending the command if it not set before. 
  expect( cmd.color ).toBeUndefined();
  // The BeatifulCommand handler returns a string that is "Color - Beauty".
  const executedCommand = await ep.sendAsync( cmd );
  expect( cmd.color ).toBe( "Black" );
  expect( executedCommand.result ).toBe( "Black - Superb" );
});

it('sendOrThrowAsync throws the CrisError.', async () => {
    const ep = new HttpCrisEndpoint(axios, crisEndpoint);
    const cmd = new BuggyCommand( true );
    try {
        await ep.sendOrThrowAsync(cmd);
        fail("Never here!");
    }
    catch (ex) {
        expect(ex instanceof CrisError);
        const cex = <CrisError>ex;
        expect(cex.errorType === "ValidationError");
    }
    cmd.emitValidationError = false;
    try {
        await ep.sendOrThrowAsync(cmd);
        fail("Never here!");
    }
    catch (ex) {
        expect(ex instanceof CrisError);
        const cex = <CrisError>ex;
        expect(cex.errorType === "ExecutionError");
    }
});
it('CrisError validation messages are SimpleMessage.', async () => {
    const ep = new HttpCrisEndpoint(axios, crisEndpoint);
    const cmd = new BuggyCommand( true );
    var executed = await ep.sendAsync(cmd);
    expect( executed.result instanceof CrisError ).toBeTruthy();
    // The executed.result is a CrisError.
    const r = <CrisError>executed.result;
    expect( r.errorType ).toBe( "ValidationError" );
    expect( r.message ).toBe( "The BuggyCommand is not valid (by design)." );
    expect( r.errors ).toEqual( ["The BuggyCommand is not valid (by design)."] );
    // The validation messages if any (it can be undefined) are available on the
    // executed command itself. 
    expect(executed.validationMessages).toBeDefined();
    expect(executed.validationMessages![0].message).toBe("This is an info from the command validation.");
    expect(executed.validationMessages![0].depth).toBe( 0 );
    expect(executed.validationMessages![0].level).toBe( UserMessageLevel.Info );

    expect(executed.validationMessages![1].message).toBe("The BuggyCommand is not valid (by design).");
    expect(executed.validationMessages![1].depth).toBe( 1 );
    expect(executed.validationMessages![1].level).toBe( UserMessageLevel.Error );
    
    expect(executed.validationMessages![2].message).toBe("This is a warning from the command validation.");
    expect(executed.validationMessages![2].depth).toBe( 1 );
    expect(executed.validationMessages![2].level).toBe( UserMessageLevel.Warn );
});
it('A command can return a SimpleMessage.', async () => {
  const ep = new HttpCrisEndpoint(axios, crisEndpoint);
  const cmd = new WithMessageCommand();
  var r = await ep.sendOrThrowAsync(cmd);
  expect( r instanceof SimpleUserMessage );
  expect( r.message.startsWith("Local server time is") );
  expect( r.depth).toBe( 0 );
  expect( r.level).toBe( UserMessageLevel.Info );
});
it('A command can return a UserMessage but in TypeScript, this is a SimpleUserMessage.', async () => {
  const ep = new HttpCrisEndpoint(axios, crisEndpoint);
  const cmd = new WithUserMessageCommand();
  var r = await ep.sendOrThrowAsync(cmd);
  expect( r instanceof SimpleUserMessage );
  expect( r.message.startsWith("I'm a UserMessage and it is ") );
  expect( r.depth ).toBe( 12 );
  expect( r.level ).toBe( UserMessageLevel.Warn );
});
