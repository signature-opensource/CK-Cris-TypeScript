using CK.Core;
using System;

namespace CK.Cris.AspNet;

/// <summary>
/// Declares the route of the commands that extend a command part: they must be posted to this route, and this route accepts
/// only them. Use the generic <see cref="CrisRouteAttribute{T}"/>.
/// <para>
/// This decorates the <see cref="IResourcePackage"/> (typically the TypeScript package) of the package that owns the route.
/// The rules (checked at setup time):
/// <list type="bullet">
///   <item>The route is under "/.cris" (the same-origin check and the cookies of "/.cris" apply to it). "/.cris/net" is reserved.</item>
///   <item>A command that matches no declared route is posted to "/.cris".</item>
///   <item>A command that matches two different routes is an error: there is no priority.</item>
/// </list>
/// The same declaration is used by the TypeScript generator (the client posts each command to its route) and by the
/// <see cref="CrisAspNetService"/> (a command is accepted only on its own route).
/// </para>
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
public abstract class CrisRouteAttribute : Attribute
{
    private protected CrisRouteAttribute( Type commandPart, string route )
    {
        CommandPart = commandPart;
        Route = route;
    }

    /// <summary>
    /// Gets the command part: the commands that extend it are posted to <see cref="Route"/>.
    /// </summary>
    public Type CommandPart { get; }

    /// <summary>
    /// Gets the route (the request path, like "/.cris/auth").
    /// </summary>
    public string Route { get; }
}

/// <summary>
/// Declares the route of the commands that extend the command part <typeparamref name="T"/>.
/// See <see cref="CrisRouteAttribute"/>.
/// </summary>
/// <typeparam name="T">The command part.</typeparam>
public sealed class CrisRouteAttribute<T> : CrisRouteAttribute where T : ICommandPart
{
    /// <summary>
    /// Initializes a new route declaration.
    /// </summary>
    /// <param name="route">The route (the request path, like "/.cris/auth").</param>
    public CrisRouteAttribute( string route )
        : base( typeof( T ), route )
    {
    }
}
