using Core.Domain.Shared;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Enums.GetLocationTypes;

internal static class GetLocationTypesEndpoint
{
    internal sealed record Response(ELocationType Name);

    internal static IResult HandleAsync(HttpContext http)
    {
        var types = Enum.GetValues<ELocationType>()
            .Where(t => t != ELocationType.Unspecified)
            .Select(t => new Response(t))
            .ToList();
        return PagedResponse.Complete(http, types);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetLocationTypesEndpoint()
        {
            app.MapGet("LocationTypes", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetLocationTypesV2")
            .WithSummary("List location types")
            .WithDescription("Returns the valid location type names, without Unspecified.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}