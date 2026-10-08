using CK.Core;
using CK.Cris.AspNet;
using CK.IO.Authentication;
using CK.TypeScript;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The package of the HTTP front of the authentication. It declares the route of the session commands
/// (<see cref="ICommandAuthenticationSession"/>): <see cref="AuthRoute"/>.
/// </summary>
[TypeScriptPackage]
[CrisRoute<ICommandAuthenticationSession>( AuthRoute )]
public class CrisAspNetAuthenticationPackage : TypeScriptPackage
{
    /// <summary>
    /// The route of the session commands: "/.cris/auth". This route checks the origin of the request and is the only one
    /// that receives the refresh cookie.
    /// </summary>
    public const string AuthRoute = "/.cris/auth";
}
