using CK.CodeGen;
using CK.Core;
using CK.Cris;
using CK.Cris.AspNet;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace CK.Setup.Cris;

/// <summary>
/// Implements the <see cref="CrisAspNetService"/>: generates the <see cref="CrisAspNetService.ICrisRouteImpl"/> on the commands.
/// </summary>
public sealed class CrisAspNetServiceImpl : CSCodeGeneratorType
{
    /// <inheritdoc />
    public override CSCodeGenerationResult Implement( IActivityMonitor monitor, Type classType, ICSCodeGenerationContext c, ITypeScope scope )
    {
        Throw.CheckArgument( "Applies only to the CrisAspNetService class.", classType == typeof( CrisAspNetService ) );

        var crisDirectory = c.CurrentRun.ServiceContainer.GetService<ICrisDirectoryServiceEngine>();
        if( crisDirectory == null ) return CSCodeGenerationResult.Retry;

        scope.CreatePassThroughConstructors( classType );

        var routes = CrisRoutes.Get( monitor, c.CurrentRun, crisDirectory );
        if( routes == null ) return CSCodeGenerationResult.Failed;

        foreach( var e in crisDirectory.CrisTypes )
        {
            if( e.Kind is not (CrisPocoKind.Command or CrisPocoKind.CommandWithResult) ) continue;
            var pocoType = c.GeneratedCode.FindOrCreateAutoImplementedClass( monitor, e.CrisPocoType.FamilyInfo.PocoClass );
            pocoType.Definition.BaseTypes.Add( new ExtendedTypeName( "CK.Cris.AspNet.CrisAspNetService.ICrisRouteImpl" ) );
            if( routes.Find( e ) is string route )
            {
                pocoType.Append( "string? CK.Cris.AspNet.CrisAspNetService.ICrisRouteImpl.CrisRoute => " ).AppendSourceString( route ).Append( ";" ).NewLine();
            }
        }
        return CSCodeGenerationResult.Success;
    }
}
