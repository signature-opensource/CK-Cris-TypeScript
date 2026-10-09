using CK.Core;
using CK.IO.Authentication.Weak;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Authentication.Weak;

/// <summary>
/// The Weak login form: it is injected in the login page and sends the <see cref="IWeakLoginCommand"/> (a weak actor logs
/// in with its name only, at the Unsafe level).
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationPackage, ZorroPackage>]
[RegisterTypeScriptType( typeof( IWeakLoginCommand ) )]
public class NgCrisAuthenticationWeakPackage : TypeScriptPackage
{
}
