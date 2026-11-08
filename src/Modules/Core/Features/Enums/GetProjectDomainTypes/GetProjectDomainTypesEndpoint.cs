using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Enums.GetProjectDomainTypes;

internal static class GetProjectDomainTypesEndpoint
{
    internal sealed record Response(string Name);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromServices] IProjectQuery projects,
        CancellationToken ct)
    {
        var names = await projects.GetProjectDomainTypesAsync(ct);
        var response = names.Select(n => new Response(n.Value)).ToList();
        return PagedResponse.Complete(http, response);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectDomainTypesEndpoint()
        {
            app.MapGet("ProjectDomainTypes", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectDomainTypesV2")
            .WithSummary("List project domain types")
            .WithDescription("Returns the names of all project domain types as a single complete page (bounded reference list — paging parameters are ignored). Use a returned name as the domainTypeName when updating a project.")
            .WithTags("Enums")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}