using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Cris.AspNet.Authentication;

[NgRoutedComponent<AuthenticationPageComponent>( Route = "" )]
[Package<NgCrisAuthenticationPackage>]
public sealed class LoginComponent : NgRoutedComponent
{
}
