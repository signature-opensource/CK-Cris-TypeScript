using CK.Auth;
using CK.Core;
using CK.Cris.AspNet;
using CK.IO.Authentication;
using CK.TypeScript;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The package of the HTTP side of the authentication. It declares the route of the session commands
/// (<see cref="ICommandAuthenticationSession"/>): <see cref="AuthRoute"/>. The other commands, wherever they are handled
/// (the authentication center's ones included), are posted to "/.cris": where a command executes is a deployment detail.
/// <para>
/// Its TypeScript side is the framework-free AuthService: the session state is updated by a hook on the HttpCrisEndpoint,
/// so any session command sent through the endpoint has its full effect.
/// </para>
/// </summary>
[TypeScriptPackage]
[CrisRoute<ICommandAuthenticationSession>( AuthRoute )]
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
}
