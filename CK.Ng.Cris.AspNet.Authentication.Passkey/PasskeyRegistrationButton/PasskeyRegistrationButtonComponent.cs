using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Cris.AspNet.Authentication.Passkey;

/// <summary>
/// The "Add a passkey" button: the application places it where the user manages their account. The registration
/// requires a recent login (the Critical level) and is refused while impersonating.
/// </summary>
[NgComponent]
[Package<NgCrisAuthenticationPasskeyPackage>]
public sealed class PasskeyRegistrationButtonComponent : NgComponent
{
}
