import { EnvironmentProviders, inject, makeEnvironmentProviders, provideAppInitializer } from '@angular/core';
import { AuthService } from '@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService';

/**
 * Provides the support of the AuthService: the application starts once the AuthService is initialized
 * (the cached token is used or the session is refreshed).
 * There is no HttpClient interceptor: the authentication applies to the Cris commands (the AuthService
 * handles the HttpCrisEndpoint).
 * @returns EnvironmentProviders that support the AuthService.
 */
export function provideNgCrisAuthenticationSupport(): EnvironmentProviders {
    return makeEnvironmentProviders( [
        provideAppInitializer( () => inject( AuthService ).isInitialized )
    ] );
}
