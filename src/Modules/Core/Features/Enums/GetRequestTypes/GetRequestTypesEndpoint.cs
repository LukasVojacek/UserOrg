using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Enums.GetRequestTypes;

internal static class GetRequestTypesEndpoint
{
    internal sealed record Response(string Name);

    private static readonly IReadOnlyList<Response> _types =
    [
        new(RequestType.Default.Name),
        new(RequestType.SelfOnboarding.Name),
        new(RequestType.ProjectResourceAssignment.Name),
        new(RequestType.HEAppECreation.Name),
    ];

    internal static IResult HandleAsync(HttpContext http) => PagedResponse.Complete(http, _types);

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetRequestTypesEndpoint()
        {
            app.MapGet("RequestTypes", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetRequestTypesV2")
            .WithSummary("List request types")
            .WithDescription("Returns all valid request types as a bare array (a single complete page; X-Total-Count header, no Link). Each item carries the stable PascalCase Name (used in request/response contracts) and the legacy integer Id.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}