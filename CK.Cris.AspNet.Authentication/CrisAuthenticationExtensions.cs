using CK.Auth;
using CK.Core;
using CK.Cris.AspNet.Authentication;
using Microsoft.AspNetCore.Builder;
using System;
using System.Linq;

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
    /// The features (assemblies) of the old authentication stack: an application uses one stack. The other old packages
    /// (CK.Cris.Auth, CK.Cris.AspNet.Auth.Basic, CK.TS.AspNet.Auth, CK.Ng.AspNet.Auth...) either depend on these or
    /// already conflict at setup time (CK.Cris.Auth defines the same CK.Auth.CrisAuthenticationService).
    /// </summary>
    static readonly string[] _oldStackFeatures = ["CK.AspNet.Auth"];

    /// <summary>
    /// Adds the <see cref="CrisAuthenticationMiddleware"/>: it must be added before the Cris middleware (UseCris).
    /// <para>
    /// This throws when the application also contains the old authentication stack (WebFrontAuth, CK.AspNet.Auth): two
    /// login systems would issue their own tokens and cookies side by side.
    /// </para>
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseCrisAuthentication( this IApplicationBuilder app )
    {
        var map = app.ApplicationServices.GetRequiredService<IStObjMap>();
        var old = map.Features.Where( f => _oldStackFeatures.Contains( f.Name, StringComparer.OrdinalIgnoreCase ) ).ToList();
        if( old.Count > 0 )
        {
            Throw.InvalidOperationException( $"The application contains the old authentication stack ({old.Select( f => f.Name ).Concatenate()}) " +
                                             $"and the new one (CK.Cris.AspNet.Authentication): an application uses one of them." );
        }
        return app.UseMiddleware<CrisAuthenticationMiddleware>();
    }
}
