using CK.Cris.AspNet;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Adds extension methods on <see cref="IApplicationBuilder"/>.
/// </summary>
public static class CrisApplicationBuilderExtension
{
    /// <summary>
    /// Injects the <see cref="CrisMiddleware"/> into the pipeline.
    /// </summary>
    /// <param name="this">This application builder.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseCris( this IApplicationBuilder @this )
    {
        return @this.UseMiddleware<CrisMiddleware>();
    }
}
