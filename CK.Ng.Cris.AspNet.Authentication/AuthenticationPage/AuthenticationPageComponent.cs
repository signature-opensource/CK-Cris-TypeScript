using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Cris.AspNet.Authentication;

[NgRoutedComponent<AppComponent>( HasRoutes = true, Route = "auth" )]
[Package<NgCrisAuthenticationPackage>]
public sealed class AuthenticationPageComponent : NgRoutedComponent
{
}
