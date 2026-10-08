using CK.IO.Authentication;
using Microsoft.AspNetCore.Http;

namespace CK.Cris.AspNet.Authentication;

/// <summary>
/// The session commands (<see cref="ICommandAuthenticationSession"/>) are accepted only on "/.cris/auth", and only them:
/// this route checks the origin of the request and is the only one that receives the refresh cookie.
/// </summary>
public sealed class SessionCommandRouteGuard : ICrisRouteGuard
{
    /// <summary>
    /// The route of the session commands: "/.cris/auth".
    /// </summary>
    public static readonly PathString AuthPath = new PathString( "/.cris/auth" );

    /// <inheritdoc />
    public string? Check( PathString route, IAbstractCommand command )
    {
        bool isAuthRoute = route.StartsWithSegments( AuthPath );
        bool isSessionCommand = command is ICommandAuthenticationSession;
        if( isSessionCommand && !isAuthRoute ) return $"A session command must be sent to '{AuthPath}'.";
        if( !isSessionCommand && isAuthRoute ) return $"Only the session commands can be sent to '{AuthPath}'.";
        return null;
    }
}
