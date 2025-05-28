using CK.Core;
using Shouldly;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace CK.Cris.AspNet.Tests;

static class LocalHelper
{
    public const string CrisUri = "/.cris/net";

    static public async Task<ICrisCallResult> GetCrisResultAsync( this PocoDirectory p, HttpResponseMessage r )
    {
        var result = p.Find<ICrisCallResult>()!.ReadJson( await r.Content.ReadAsByteArrayAsync() );
        Throw.DebugAssert( result != null );
        return result;
    }

    static public async Task<ICrisCallResult> GetCrisResultWithCorrelationIdSetToNullAsync( this PocoDirectory p, HttpResponseMessage r )
    {
        var result = await GetCrisResultAsync( p, r );
        result.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        result.CorrelationId = null;
        return result;
    }

    static public async Task<ICrisCallResult> GetValidationErrorsAsync( this PocoDirectory p, HttpResponseMessage r, params SimpleUserMessage[] messages )
    {
        var result = await GetCrisResultAsync( p, r );
        result.ValidationMessages.ShouldNotBeNull();
        result.ValidationMessages!.Select( m => m.AsSimpleUserMessage() ).ShouldBe( messages );
        result.Result.ShouldNotBeNull().ShouldBeAssignableTo<ICrisResultError>();
        var e = (ICrisResultError)result.Result!;
        e.IsValidationError.ShouldBeTrue();
        e.Errors.Select( m => m.AsSimpleUserMessage() ).ShouldBe( messages );
        return result;
    }

}
