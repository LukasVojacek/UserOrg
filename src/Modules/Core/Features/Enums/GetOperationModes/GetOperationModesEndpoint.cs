using Core.Domain.Shared;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Enums.GetOperationModes;

internal static class GetOperationModesEndpoint
{
    internal sealed record Response(string Name);

    internal static IResult HandleAsync(HttpContext http)
    {
        var modes = Enum.GetValues<EOperationMode>()
            .Select(m => new Response(m.ToString()))
            .ToList();
        return PagedResponse.Complete(http, modes);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetOperationModesEndpoint()
        {
            app.MapGet("OperationModes", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetOperationModesV2")
            .WithSummary("List operation modes")
            .WithDescription("Returns all valid operation mode identifiers and names.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}