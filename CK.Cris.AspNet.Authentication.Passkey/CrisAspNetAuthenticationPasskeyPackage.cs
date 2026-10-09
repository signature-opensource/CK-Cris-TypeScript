using CK.Core;
using CK.IO.Authentication.Passkey;
using CK.TypeScript;

namespace CK.Cris.AspNet.Authentication.Passkey;

/// <summary>
/// The passkey (WebAuthn) logins of the HTTP front, on the TypeScript side: the framework-free <c>PasskeyService</c>
/// sends the passkey commands through the Cris endpoint and runs the browser's part of the ceremonies
/// (<c>navigator.credentials</c>). The <c>AuthService</c> has no passkey member: this package exists only when passkeys
/// are wanted ("Package First"). The passkey commands are session commands (routed to "/.cris/auth"): the AuthService
/// follows the login like any other.
/// </summary>
[TypeScriptPackage]
[Requires<CrisAspNetAuthenticationPackage>]
[RegisterTypeScriptType( typeof( IStartPasskeyLoginCommand ) )]
[RegisterTypeScriptType( typeof( IPasskeyLoginCommand ) )]
[RegisterTypeScriptType( typeof( IStartPasskeyRegistrationCommand ) )]
[RegisterTypeScriptType( typeof( IPasskeyRegistrationCommand ) )]
[TypeScriptFile( "PasskeyService.ts", "PasskeyService" )]
public class CrisAspNetAuthenticationPasskeyPackage : TypeScriptPackage
{
}
