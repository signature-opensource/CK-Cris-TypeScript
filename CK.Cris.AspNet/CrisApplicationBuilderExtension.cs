using CK.Cris.AspNet;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Adds extension methods on <see cref="IApplicationBuilder"/> and <see cref="WebApplicationBuilder"/>.
/// </summary>
public static class CrisApplicationBuilderExtension
{
    /// <summary>
    /// Injects the <see cref="CrisMiddleware"/> into the pipeline in one gesture on the builder (the "Enable" convention
    /// of CK.AspNet: this is idempotent). <see cref="CrisAspNetHosting"/> contributes it to the web applications of the host.
    /// </summary>
    /// <param name="builder">This builder.</param>
    /// <returns>This builder.</returns>
    public static WebApplicationBuilder EnableCris( this WebApplicationBuilder builder )
    {
        if( builder.TryEnable( "Cris" ) )
        {
            builder.AppendApplicationBuilder( app => app.UseCris() );
        }
        return builder;
    }

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
