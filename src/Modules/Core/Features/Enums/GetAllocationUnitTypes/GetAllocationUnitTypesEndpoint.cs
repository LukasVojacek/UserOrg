using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Core.Features.Enums.GetAllocationUnitType;

internal static class GetAllocationUnitTypesEndpoint
{
    internal sealed record Response(EAllocationUnitType Name);

    internal static IResult HandleAsync(HttpContext http)
    {
        var types = Enum.GetValues<EAllocationUnitType>()
            .Where(t => t != EAllocationUnitType.None)
            .Select(t => new Response(t))
            .ToList();
        return PagedResponse.Complete(http, types);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetAllocationUnitTypesEndpoint()
        {
            app.MapGet("AllocationUnitTypes", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetAllocationUnitTypesV2")
            .WithSummary("List allocation unit types")
            .WithDescription("Returns the valid allocation unit type names, without None. A name from this list is what AllocationUnitType takes on the assignment writes.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}