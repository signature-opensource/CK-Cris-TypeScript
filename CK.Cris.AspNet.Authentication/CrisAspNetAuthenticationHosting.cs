using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// Contributes <see cref="CrisAuthenticationExtensions.EnableCrisAuthentication"/> to the web applications of the host
/// (<see cref="WebApplicationContributions"/>): a host that applies the contributions (the "HttpEndpoint" feature of
/// CK.AppIdentity.HttpEndpoint) authenticates the "/.cris" requests without calling anything.
/// </summary>
public sealed class CrisAspNetAuthenticationHosting : IRealObject
{
    void ConfigureServices( StObjContextRoot.ServiceRegister services )
    {
        WebApplicationContributions.FindOrAdd( services.Services ).Add( web => web.EnableCrisAuthentication() );
    }
}
