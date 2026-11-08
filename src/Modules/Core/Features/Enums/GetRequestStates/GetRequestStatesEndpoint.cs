using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Enums.GetRequestStates;

internal static class GetRequestStatesEndpoint
{
    internal sealed record Response(string Name);

    private static readonly IReadOnlyList<Response> _states =
    [
        new(RequestState.Pending.Name),
        new(RequestState.PendingForPIAccept.Name),
        new(RequestState.PendingForHPCProject.Name),
        new(RequestState.Accepted.Name),
        new(RequestState.Rejected.Name),
    ];

    internal static IResult HandleAsync(HttpContext http) => PagedResponse.Complete(http, _states);

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetRequestStatesEndpoint()
        {
            app.MapGet("RequestStates", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetRequestStatesV2")
            .WithSummary("List request states")
            .WithDescription("Returns all valid request states as a bare array (a single complete page; X-Total-Count header, no Link). Each item carries the stable PascalCase Name (used in request/response contracts) and the legacy integer Id.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}