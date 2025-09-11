using CK.Core;
using CK.Cris.AspNet;
using CK.Poco.Exc.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System.Buffers;

namespace CK.Cris.WebApi;

/// <summary>
/// Exposes extensions methods to encapsulate WebApi methods. Each request expects an <see cref="IAbstractCommand"/>.
/// The incoming Cris command will be read with its associated <see cref="IPocoFactory"/>
/// and handled through <see cref="CrisAspNetService.HandleRequestAsync"/>.
/// </summary>
public static class CrisWebApiExtensions
{
    public const int Status456HandlingError = 456;

    public static RouteHandlerBuilder MapApiPost<T>( this IEndpointRouteBuilder app, string pattern, CommandRequestReader? cmdReader = null )
        where T : class, IAbstractCommand
    {
        return app.MapPost( pattern,
                            ( IActivityMonitor m,
                              HttpRequest request,
                              [FromServices] CrisAspNetService crisAspNetService,
                              [FromServices] PocoDirectory pocoDir,
                              CurrentCultureInfo currentCulture ) =>
        {
            return HandleCKApiRequestAsync<T>( m, request, crisAspNetService, currentCulture, pocoDir, cmdReader, PocoJsonImportOptions.Default );
        } )
        .Accepts<T>( "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status400BadRequest, "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status500InternalServerError, "application/json" );
    }

    public static RouteHandlerBuilder MapApiPatch<T>( this IEndpointRouteBuilder app, string pattern, CommandRequestReader? cmdReader = null )
        where T : class, IAbstractCommand
    {
        return app.MapPatch( pattern,
                             ( IActivityMonitor m,
                               HttpRequest request,
                               [FromServices] CrisAspNetService crisAspNetService,
                               [FromServices] PocoDirectory pocoDir,
                               CurrentCultureInfo currentCulture ) =>
        {
            return HandleCKApiRequestAsync<T>( m, request, crisAspNetService, currentCulture, pocoDir, cmdReader, PocoJsonImportOptions.Default );
        } )
        .Accepts<T>( "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status400BadRequest, "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status500InternalServerError, "application/json" );
    }

    public static RouteHandlerBuilder MapApiPut<T>( this IEndpointRouteBuilder app, string pattern, CommandRequestReader? cmdReader = null )
        where T : class, IAbstractCommand
    {
        return app.MapPut( pattern,
                           ( IActivityMonitor m,
                             HttpRequest request,
                             [FromServices] CrisAspNetService crisAspNetService,
                             [FromServices] PocoDirectory pocoDir,
                             CurrentCultureInfo currentCulture ) =>
        {
            return HandleCKApiRequestAsync<T>( m, request, crisAspNetService, currentCulture, pocoDir, cmdReader, PocoJsonImportOptions.Default );
        } )
        .Accepts<T>( "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status400BadRequest, "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status500InternalServerError, "application/json" );
    }

    public static RouteHandlerBuilder MapApiDelete<T>( this IEndpointRouteBuilder app, string pattern, CommandRequestReader? cmdReader = null )
        where T : class, IAbstractCommand
    {
        return app.MapDelete( pattern,
                              ( IActivityMonitor m,
                                HttpRequest request,
                                [FromServices] CrisAspNetService crisAspNetService,
                                [FromServices] PocoDirectory pocoDir,
                                CurrentCultureInfo currentCulture ) =>
        {
            return HandleCKApiRequestAsync<T>( m, request, crisAspNetService, currentCulture, pocoDir, cmdReader, PocoJsonImportOptions.Default );
        } )
        .Accepts<T>( "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status400BadRequest, "application/json" )
        .Produces<ICrisResultError>( StatusCodes.Status500InternalServerError, "application/json" );
    }

    static async Task<IResult> HandleCKApiRequestAsync<T>( IActivityMonitor m,
                                                           HttpRequest request,
                                                           CrisAspNetService crisAspNetService,
                                                           CurrentCultureInfo currentCulture,
                                                           PocoDirectory pocoDir,
                                                           CommandRequestReader? cmdReader = null,
                                                           PocoJsonImportOptions? readOptions = null )
        where T : class, IAbstractCommand
    {
        var (crisResult, typeFilterName) = await crisAspNetService.HandleRequestAsync( m,
                                                                                       request,
                                                                                       cmdReader ?? ReadCommandAsync,
                                                                                       currentCulture,
                                                                                       readOptions );

        return crisResult.ToCrisWebResult( typeFilterName );

        static ValueTask<IAbstractCommand?> ReadCommandAsync( IActivityMonitor monitor,
                                                              HttpRequest request,
                                                              PocoDirectory pocoDirectory,
                                                              UserMessageCollector messageCollector,
                                                              ReadOnlySequence<byte> payload,
                                                              PocoJsonImportOptions readOptions )
        {
            var cmd = pocoDirectory.Find<T>()?.ReadJson( payload, readOptions );
            if( cmd == null )
            {
                messageCollector.Error( "Received a null Poco.", "Cris.AspNet.ReceiveNullPoco" );
                return ValueTask.FromResult<IAbstractCommand?>( null );
            }
            if( cmd is not T )
            {
                messageCollector.Error( $"Received Poco is not a {typeof( T ):N} but a '{((IPocoGeneratedClass)cmd).Factory.Name}'.", "Cris.AspNet.NotACommand" );
                return ValueTask.FromResult<IAbstractCommand?>( null );
            }

            return ValueTask.FromResult<IAbstractCommand?>( cmd );
        }
    }

    public static CrisWebResult ToCrisWebResult( this ICrisCallResult crisCall, string typeFilterName )
    {
        var pocoDir = ((IPocoGeneratedClass)crisCall).Factory.PocoDirectory;

        if( crisCall.Result is ICrisResultError )
        {
            return ToHttpError( crisCall, typeFilterName, pocoDir );
        }

        return new CrisWebResult( pocoDir, crisCall, typeFilterName, statusCode: 200 );
    }

    public static CrisWebResult ToHttpError( ICrisCallResult crisCall, string typeFilterName, PocoDirectory pocoDir )
    {
        Throw.CheckArgument( crisCall.Result is not null );
        var crisResultError = (ICrisResultError)crisCall.Result;
        if( crisResultError.IsValidationError )
        {
            return new CrisWebResult( pocoDir, crisCall, typeFilterName, statusCode: 400 );
        }

        return new CrisWebResult( pocoDir, crisCall, typeFilterName, statusCode: Status456HandlingError );
    }
}
