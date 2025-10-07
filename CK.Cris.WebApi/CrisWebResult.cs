using CK.Core;
using CK.Poco.Exc.Json;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace CK.Cris.WebApi;

public class CrisWebResult : IResult
{
    public CrisWebResult( PocoDirectory pocoDirectory, ICrisCallResult crisResult, string typeFilterName, int statusCode )
    {
        PocoDirectory = pocoDirectory;
        CrisResult = crisResult;
        TypeFilterName = typeFilterName;
        StatusCode = statusCode;
    }

    public PocoDirectory PocoDirectory { get; }
    public ICrisCallResult CrisResult { get; }
    public string TypeFilterName { get; }
    public int StatusCode { get; }

    public virtual Task ExecuteAsync( HttpContext httpContext )
    {
        httpContext.Response.StatusCode = StatusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        var pocoExportOpts = new PocoJsonExportOptions()
        {
            UserMessageFormat = UserMessageSimplifiedFormat.String,
            TypeFilterName = TypeFilterName
        };

        using( var w = new Utf8JsonWriter( httpContext.Response.BodyWriter, pocoExportOpts.WriterOptions ) )
        {
            CrisResult.WriteJson( w, new PocoJsonWriteContext( PocoDirectory, pocoExportOpts ), withType: false );
        }
        
        return Task.CompletedTask;
    }
}
