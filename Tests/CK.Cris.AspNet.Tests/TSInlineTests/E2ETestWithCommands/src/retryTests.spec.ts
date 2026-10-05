import axios, { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from "axios";
import { HttpCrisEndpoint, CrisError, BeautifulCommand } from "@local/ck-gen";

const crisEndpoint = CKTypeScriptEnv["CRIS_ENDPOINT_URL"] ?? "";
const withEndpoint = crisEndpoint ? it : it.skip;

/**
 * Creates an axios instance whose requests fail with the error returned by fail (no error when undefined).
 */
function createAxios( fail: ( config: InternalAxiosRequestConfig ) => AxiosError | undefined ): AxiosInstance {
  const ax = axios.create();
  ax.interceptors.request.use( config => {
    const error = fail( config );
    if( error ) throw error;
    return config;
  } );
  return ax;
}

/** An error from a server that answered with a status. */
function serverError( status: number, config: InternalAxiosRequestConfig ): AxiosError {
  return new AxiosError( `Request failed with status code ${status}`,
                         AxiosError.ERR_BAD_RESPONSE,
                         config,
                         undefined,
                         <any>{ status, statusText: "", headers: {}, config, data: "" } );
}

/** An error without response: the server cannot be reached. */
function networkError( config: InternalAxiosRequestConfig ): AxiosError {
  return new AxiosError( "Network Error", AxiosError.ERR_NETWORK, config );
}

it( 'a server error while getting the ambient values fails the command instead of hanging.', async () => {
  // No request reaches a server here: the url is never used.
  const ep = new HttpCrisEndpoint( createAxios( c => serverError( 500, c ) ), "http://localhost/.cris" );
  const executed = await ep.sendAsync( new BeautifulCommand( "Never sent" ) );
  expect( executed.result ).toBeInstanceOf( CrisError );
  const error = <CrisError>executed.result;
  expect( error.errorType ).toBe( "ExecutionError" );
  expect( error.message ).toContain( "HTTP 500" );
  expect( ep.isConnected ).toBe( false );
});

withEndpoint( 'a server error does not disconnect the endpoint.', async () => {
  let fail = false;
  const ep = new HttpCrisEndpoint( createAxios( c => fail ? serverError( 500, c ) : undefined ), crisEndpoint );
  await ep.sendAsync( new BeautifulCommand( "Connected" ) );
  expect( ep.isConnected ).toBe( true );

  fail = true;
  const executed = await ep.sendAsync( new BeautifulCommand( "Failed" ) );
  expect( executed.result ).toBeInstanceOf( CrisError );
  expect( (<CrisError>executed.result).errorType ).toBe( "ExecutionError" );
  expect( ep.isConnected ).toBe( true );
});

withEndpoint( 'an unreachable server is retried with a backoff until it answers.', async () => {
  let attempts = 0;
  const ep = new HttpCrisEndpoint( createAxios( c => ++attempts <= 3 ? networkError( c ) : undefined ), crisEndpoint );
  ep.retryMinDelay = 50;
  const start = Date.now();
  const ambientValues = await ep.updateAmbientValuesAsync();
  const elapsed = Date.now() - start;
  expect( ambientValues.color ).toBe( "Red" );
  expect( ep.isConnected ).toBe( true );
  // 3 failures then the success.
  expect( attempts ).toBe( 4 );
  // Waited 50, 100 and 200 ms (with a little slack for the timers).
  expect( elapsed ).toBeGreaterThanOrEqual( 340 );
});
