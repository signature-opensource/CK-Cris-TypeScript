using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Cris.AspNet.Authentication;

[NgRoutedComponent<AppComponent>( HasRoutes = true, Route = "" )]
[Package<NgCrisAuthenticationPackage>]
public sealed class PrivatePageComponent : NgRoutedComponent, INgPrivatePageComponent
{
}
