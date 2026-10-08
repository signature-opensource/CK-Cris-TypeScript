using CK.Core;
using Microsoft.AspNetCore.Http;

namespace CK.Cris.AspNet;

/// <summary>
/// Decides whether a command is accepted on a route (the request path): <see cref="CrisAspNetService"/> refuses a command as
/// soon as one guard refuses it, before its validation. There can be many guards; none by default.
/// <para>
/// For instance, the authentication accepts the session commands only on their dedicated route.
/// </para>
/// </summary>
[IsMultiple]
public interface ICrisRouteGuard : ISingletonAutoService
{
    /// <summary>
    /// Checks a command. Returns null when it is accepted, the reason of the refusal otherwise.
    /// </summary>
    /// <param name="route">The request path.</param>
    /// <param name="command">The command read from the request.</param>
    /// <returns>Null or the reason of the refusal.</returns>
    string? Check( PathString route, IAbstractCommand command );
}
