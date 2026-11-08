using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Meta.GetVersion;

internal static class GetVersionEndpoint
{
    internal sealed record Response(string Version);

    internal static IResult HandleAsync()
    {
        var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "0.0.0.1";
        return Results.Ok(new Response(version));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetVersionEndpoint()
        {
            app.MapGet("Version", HandleAsync)
                .AllowAnonymous()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetVersionV2")
            .WithSummary("Get application version")
            .WithDescription("Returns the current application version. No authentication required.")
            .WithTags("Meta")
            .Produces<Response>(StatusCodes.Status200OK);
    }
}