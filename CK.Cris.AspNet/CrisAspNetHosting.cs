using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Builder;

namespace CK.Cris.AspNet;

/// <summary>
/// Contributes <see cref="CrisApplicationBuilderExtension.EnableCris"/> to the web applications of the host
/// (<see cref="WebApplicationContributions"/>): a host that applies the contributions (the "HttpEndpoint" feature of
/// CK.AppIdentity.HttpEndpoint) serves the "/.cris" endpoints without calling anything.
/// </summary>
public sealed class CrisAspNetHosting : IRealObject
{
    void ConfigureServices( StObjContextRoot.ServiceRegister services )
    {
        WebApplicationContributions.FindOrAdd( services.Services ).Add( web => web.EnableCris() );
    }
}
