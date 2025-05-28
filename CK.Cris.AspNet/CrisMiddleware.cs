using CK.Core;
using CK.Poco.Exc.Json;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace CK.Cris.AspNet;

/// <summary>
/// Middleware that handles "/.cris" requests from javascript frontend (and "/.cris/net" for Http command senders).
/// </summary>
public class CrisMiddleware
{
    static readonly PathString _crisPath = new PathString( "/.cris" );
    static readonly PathString _netPath = new PathString( "/net" );
    readonly RequestDelegate _next;
    readonly CrisAspNetService _service;

    /// <summary>
    /// Initializes a new <see cref="CrisMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="service">The Cris AspNet service.</param>
    public CrisMiddleware( RequestDelegate next, CrisAspNetService service )
    {
        Throw.CheckNotNullArgument( next );
        _next = next;
        _service = service;
    }

    /// <summary>
    /// Handles the command on "/.cris" path.
    /// </summary>
    /// <param name="ctx">The current context.</param>
    /// <param name="monitor">The request scoped monitor.</param>
    /// <returns>The awaitable.</returns>
    public async Task InvokeAsync( HttpContext ctx, IActivityMonitor monitor )
    {
        if( ctx.Request.Path.StartsWithSegments( _crisPath, out PathString remainder ) )
        {
            if( !HttpMethods.IsPost( ctx.Request.Method ) )
            {
                ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            }
            else
            {
                ctx.Response.StatusCode = 200;
                // Front applications uses /.cris.
                // HttpCrisSender uses /.cris/net path.
                bool isNetPath = remainder.StartsWithSegments( _netPath );

                // For front applications the TypeFilterName is or starts with "TypeScript", the optional "TypeFilterName" query string 
                // sets this and a PocoJsonImportOptions is computed from it (with other options) by the
                // public CrisAspNetService.TryCreateJsonImportOptions helper.
                //
                // For a HttpCrisSender we use the PocoJsonImportOptions.Default ("AllExchangeable" type set). This is NOT ideal:
                // the /.cris/net path allows all the exchangeable commands, but the HttpCrisSender is a temporary solution that will
                // be replaced with the Transport feature. If it stays here, it SHOULD be refactored to use a type set (or a family)
                // explicitly defined or its identity should be enforced.
                //
                var readOptions = isNetPath
                                    ? PocoJsonImportOptions.Default
                                    : null;

                // The public CrisAspNetService.StandardReadCommandAsync helper checks that the TypeFilterName is (or starts with) "TypeScript".
                // The internal DoStandardReadAsync does the job with no check.
                CommandRequestReader reader = isNetPath
                                                ? CrisAspNetService.DoStandardReadAsync
                                                : CrisAspNetService.StandardReadCommandAsync;

                var (result, typeFilterName) = await _service.DoHandleAsync( monitor,
                                                                             ctx.Request,
                                                                             reader,
                                                                             currentCultureInfo: null,
                                                                             readOptions );

                PocoJsonExportOptions writeOptions;
                if( isNetPath )
                {
                    // There is no option for /.cris/net: using AlwaysExportSimpleUserMessage doesn't make sense.
                    // This uses the "AllExportable" Poco type set.
                    writeOptions = PocoJsonExportOptions.Default;
                }
                else
                {
                    // For front applications, the writer uses the TypeFilterName of the input.
                    // The CreateJsonExportOptions helper creates a PocoJsonExportOptions from the request and
                    // the TypeFilterName obtained for the reader.
                    // This always use AlwaysExportSimpleUserMessage = true.
                    writeOptions = _service.CreateJsonExportOptions( ctx.Request, typeFilterName, skipValidation: true );
                }

                // If the returned result (that is a IAspNetCrisResult Poco) is fotlered out, this is a serious issue: this type
                // is automatically registered in the "TypeScript" set by CK.TypeScript.
                if( !_service._pocoDirectory.WriteJson( ctx.Response.BodyWriter, result, withType: false, writeOptions ) )
                {
                    Throw.CKException( $"Poco '{result.GetType().FullName}' has been filtered out by '{typeFilterName}' ExchangeableRuntimeFilter." );
                }
            }
        }
        else
        {
            await _next( ctx );
        }
    }

}
