using CK.Auth;
using CK.Core;
using CK.Cris.AspNet;
using CK.IO.Authentication;
using CK.TypeScript;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The package of the HTTP front of the authentication. It declares the route of the session commands
/// (<see cref="ICommandAuthenticationSession"/>): <see cref="AuthRoute"/>, and the one of the commands executed by the
/// authentication center (<see cref="ICommandAuthCenter"/>): <see cref="AuthCenterRoute"/>.
/// <para>
/// Its TypeScript side is the framework-free AuthService: the session state is updated by a hook on the HttpCrisEndpoint,
/// so any session command sent through the endpoint has its full effect.
/// </para>
/// </summary>
[TypeScriptPackage]
[CrisRoute<ICommandAuthenticationSession>( AuthRoute )]
[CrisRoute<ICommandAuthCenter>( AuthCenterRoute )]
[RegisterTypeScriptType( typeof( IRefreshSessionCommand ) )]
[RegisterTypeScriptType( typeof( ILogoutSessionCommand ) )]
[RegisterTypeScriptType( typeof( IImpersonateCommand ) )]
[RegisterTypeScriptType( typeof( IClearImpersonationCommand ) )]
[RegisterTypeScriptType( typeof( IStartLoginCommand ) )]
[RegisterTypeScriptType( typeof( IGetLoginSchemesQCommand ) )]
[TypeScriptFile( "AuthService.ts", "AuthService" )]
public class CrisAspNetAuthenticationPackage : TypeScriptPackage
{
    /// <summary>
    /// The route of the session commands: "/.cris/auth". This route checks the origin of the request and is the only one
    /// that receives the refresh cookie.
    /// </summary>
    public const string AuthRoute = "/.cris/auth";

    /// <summary>
    /// The route of the commands executed by the authentication center (<see cref="ICommandAuthCenter"/>): "/.cris/auth-center".
    /// A front relays them to its remote authentication center with the caller's proof. The refresh cookie
    /// (<c>Path=/.cris/auth</c>) is not sent here: "/.cris/auth" doesn't path-match "/.cris/auth-center".
    /// </summary>
    public const string AuthCenterRoute = "/.cris/auth-center";
}
