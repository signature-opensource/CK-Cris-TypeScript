using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Cris.AspNet.Authentication;

[NgRoutedComponent<AuthenticationPageComponent>( RegistrationMode = RouteRegistrationMode.Lazy )]
[Package<NgCrisAuthenticationPackage>]
public sealed class LogoutComponent : NgRoutedComponent
{
}
