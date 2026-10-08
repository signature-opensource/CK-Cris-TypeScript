using CK.Core;
using CK.IO.Authentication.Basic;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Ng.Cris.AspNet.Authentication.Basic;

/// <summary>
/// The Basic login form: it is injected in the login page and sends the <see cref="IBasicLoginCommand"/>.
/// </summary>
[TypeScriptPackage]
[Requires<NgCrisAuthenticationPackage, ZorroPackage>]
[RegisterTypeScriptType( typeof( IBasicLoginCommand ) )]
public class NgCrisAuthenticationBasicPackage : TypeScriptPackage
{
}
