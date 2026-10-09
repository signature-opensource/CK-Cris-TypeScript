using CK.Core;
using CK.Cris.AspNet.Authentication.Passkey;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Authentication.Passkey;

/// <summary>
/// The passkey buttons: "Sign in with a passkey" is injected in the login page (it shows only when the "Passkey"
/// scheme is available and the browser supports passkeys), "Add a passkey" (<c>ck-passkey-registration-button</c>) is
/// placed by the application where the user manages their account.
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationPackage, ZorroPackage, CrisAspNetAuthenticationPasskeyPackage>]
public class NgCrisAuthenticationPasskeyPackage : TypeScriptPackage
{
}
