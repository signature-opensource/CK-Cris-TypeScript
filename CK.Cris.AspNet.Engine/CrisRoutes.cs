using CK.Core;
using CK.Cris;
using CK.Cris.AspNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CK.Setup.Cris;

/// <summary>
/// The routes of the commands, computed once per run from the <see cref="CrisRouteAttribute"/> declarations.
/// It is used by the generated C# (<see cref="CrisAspNetServiceImpl"/>) and by the TypeScript generator, so the
/// client and the server always agree.
/// </summary>
sealed partial class CrisRoutes
{
    static readonly object _key = new object();
    readonly Dictionary<CrisType, string> _routes;

    CrisRoutes( Dictionary<CrisType, string> routes ) => _routes = routes;

    /// <summary>
    /// Gets the declared route of a command. Null for the default route.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The declared route or null.</returns>
    public string? Find( CrisType command ) => _routes.GetValueOrDefault( command );

    /// <summary>
    /// Gets the routes of the current run. Null if a declaration is invalid (errors are logged once).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="run">The current run.</param>
    /// <param name="crisDirectory">The Cris engine service.</param>
    /// <returns>The routes or null on error.</returns>
    public static CrisRoutes? Get( IActivityMonitor monitor, IGeneratedBinPath run, ICrisDirectoryServiceEngine crisDirectory )
    {
        if( run.Memory.TryGetValue( _key, out var cached ) ) return cached as CrisRoutes;
        var r = Create( monitor, run, crisDirectory );
        run.Memory[_key] = (object?)r ?? _key;
        return r;
    }

    [GeneratedRegex( @"^/\.cris(/[a-z0-9\-]+)+$" )]
    private static partial Regex RouteRegex();

    static CrisRoutes? Create( IActivityMonitor monitor, IGeneratedBinPath run, ICrisDirectoryServiceEngine crisDirectory )
    {
        bool success = true;
        var declarations = new Dictionary<Type, (string Route, Type Declarer)>();
        foreach( var attrCache in run.EngineMap.AllTypesAttributesCache.Values )
        {
            var t = attrCache.Type;
            foreach( var a in t.GetCustomAttributes<CrisRouteAttribute>( inherit: false ) )
            {
                if( !typeof( IResourcePackage ).IsAssignableFrom( t ) )
                {
                    monitor.Error( $"[CrisRoute] on '{t:N}': a route must be declared on the IResourcePackage of the package that owns it." );
                    success = false;
                }
                if( !RouteRegex().IsMatch( a.Route ) || a.Route == "/.cris/net" )
                {
                    monitor.Error( $"[CrisRoute] on '{t:N}': invalid route '{a.Route}'. A route is under \"/.cris\" (lowercase letters, digits and '-' segments) and \"/.cris/net\" is reserved." );
                    success = false;
                }
                if( declarations.TryGetValue( a.CommandPart, out var exists ) )
                {
                    if( exists.Route != a.Route )
                    {
                        monitor.Error( $"Command part '{a.CommandPart:N}' has two routes: '{exists.Route}' (declared by '{exists.Declarer:N}') and '{a.Route}' (declared by '{t:N}')." );
                        success = false;
                    }
                }
                else
                {
                    declarations.Add( a.CommandPart, (a.Route, t) );
                }
            }
        }
        var routes = new Dictionary<CrisType, string>();
        if( declarations.Count > 0 )
        {
            foreach( var e in crisDirectory.CrisTypes )
            {
                if( e.Kind is not (CrisPocoKind.Command or CrisPocoKind.CommandWithResult) ) continue;
                // The Poco class implements all the interfaces of the family (extensions included).
                var pocoClass = e.CrisPocoType.FamilyInfo.PocoClass;
                var matches = declarations.Where( d => d.Key.IsAssignableFrom( pocoClass ) ).ToList();
                if( matches.Count == 0 ) continue;
                var distinct = matches.Select( m => m.Value.Route ).Distinct().ToList();
                if( distinct.Count > 1 )
                {
                    monitor.Error( $"Command '{e.PocoName}' has more than one route: {matches.Select( m => $"'{m.Value.Route}' (by its part '{m.Key:N}')" ).Concatenate()}. A command has a single route." );
                    success = false;
                }
                else
                {
                    routes.Add( e, distinct[0] );
                }
            }
            monitor.Info( $"Cris routes: {declarations.Select( d => $"'{d.Key:N}' => '{d.Value.Route}'" ).Concatenate()} ({routes.Count} commands)." );
        }
        return success ? new CrisRoutes( routes ) : null;
    }
}
