import { EnvironmentProviders, inject, makeEnvironmentProviders, provideAppInitializer } from '@angular/core';
import { AuthService } from '@local/ck-gen/CK/AspNet/Auth';
import { HttpCrisEndpoint } from '@local/ck-gen/CK/Cris/HttpCrisEndpoint';

/**
 * Provides support providers for Cris and WFA pairing:
 * - HttpCrisEndpoint's updateAmbientValues() method will be called whenever AuthenticationInfo changes.
 * @returns  EnvironmentProviders that support the HttpCrisEndpoint and AuthService working together.
 */
export function provideNgCrisAspNetAuthSupport(): EnvironmentProviders {
    return makeEnvironmentProviders([
        provideAppInitializer( updateAmbientValuesOnAuthChangeAsync )
    ]);
}

async function updateAmbientValuesOnAuthChangeAsync(): Promise<void> {
    const a = inject( AuthService );
    const h = inject( HttpCrisEndpoint );

    await a.isInitialized;
    a.addOnChange( async () => await h.updateAmbientValuesAsync() );
}
