import axios from "axios";
import { HttpCrisEndpoint, ICommand, ExecutedCommand, AmbientValuesCollectCommand, BeautifulCommand } from "@local/ck-gen";

// Trick from https://stackoverflow.com/a/77047461/190380
if( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout(20 * 60 * 1000 ); // 20 minutes

const crisEndpoint = CKTypeScriptEnv["CRIS_ENDPOINT_URL"] ?? "";
const withEndpoint = crisEndpoint ? it : it.skip;

/**
 * An endpoint that counts the requests of the ambient values, and that can hold the next one until it is released:
 * a request "in flight" while another call happens (after a login, a logout...).
 */
class GatedEndpoint extends HttpCrisEndpoint {
  public ambientRequests = 0;
  public gate: Promise<void>|undefined;

  protected override async doSendAsync<T>( command: ICommand<T> ): Promise<ExecutedCommand<T>> {
    if( command instanceof AmbientValuesCollectCommand ) {
      ++this.ambientRequests;
      const g = this.gate;
      this.gate = undefined;
      if( g ) await g;
    }
    return super.doSendAsync( command );
  }
}

withEndpoint( 'a single call sends a single request', async () => {
  const ep = new GatedEndpoint( axios, crisEndpoint );
  const a = await ep.updateAmbientValuesAsync();
  expect( a.color ).toBe( 'Red' );
  expect( ep.ambientRequests ).toBe( 1 );
} );

withEndpoint( 'a call while the ambient values are fetched gets them from a request sent after it', async () => {
  const ep = new GatedEndpoint( axios, crisEndpoint );
  let release!: () => void;
  ep.gate = new Promise<void>( resolve => release = resolve );
  // The first request is in flight: it has been sent with the authentication of this moment.
  const first = ep.updateAmbientValuesAsync();
  // The authentication changes (a login, a logout): its values must come from a request sent after this call.
  const second = ep.updateAmbientValuesAsync();
  expect( second ).toBe( first );
  release();
  const a = await second;
  expect( a.color ).toBe( 'Red' );
  expect( ep.ambientRequests ).toBe( 2 );
} );

withEndpoint( 'a command waits for the values of the last call', async () => {
  const ep = new GatedEndpoint( axios, crisEndpoint );
  let release!: () => void;
  ep.gate = new Promise<void>( resolve => release = resolve );
  const update = ep.updateAmbientValuesAsync();
  // The command waits for the ambient values (they are being fetched), then a newer call happens.
  const sent = ep.sendAsync( new BeautifulCommand( "Gorgeous" ) );
  ep.updateAmbientValuesAsync();
  release();
  await update;
  const executed = await sent;
  expect( executed.result ).toBe( "Red - Gorgeous" );
  expect( ep.ambientRequests ).toBe( 2 );
} );
