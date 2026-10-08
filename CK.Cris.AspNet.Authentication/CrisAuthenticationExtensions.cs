using CK.Auth;
using CK.Core;
using CK.Cris.AspNet.Authentication;
using Microsoft.AspNetCore.Builder;
using System;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures the authentication of the "/.cris" requests.
/// </summary>
public static class CrisAuthenticationExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="HttpAuthenticationContext"/>, and the scoped <see cref="AuthenticationInfo"/> and
    /// <see cref="IAuthenticationSessionEndpoint"/> it provides.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddCrisAuthentication( this IServiceCollection services )
    {
        services.AddScoped<HttpAuthenticationContext>();
        services.AddScoped( sp => sp.GetRequiredService<HttpAuthenticationContext>().Info );
        services.AddScoped<IAuthenticationSessionEndpoint>( sp => sp.GetRequiredService<HttpAuthenticationContext>().Endpoint
                                                                  ?? throw new InvalidOperationException( "No session endpoint outside of '/.cris'." ) );
        return services;
    }

    /// <summary>
    /// Adds the <see cref="CrisAuthenticationMiddleware"/>: it must be added before the Cris middleware (UseCris).
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseCrisAuthentication( this IApplicationBuilder app ) => app.UseMiddleware<CrisAuthenticationMiddleware>();
}
