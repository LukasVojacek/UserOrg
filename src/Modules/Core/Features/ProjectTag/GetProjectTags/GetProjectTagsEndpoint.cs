using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.ProjectTag.GetProjectTags;

internal static class GetProjectTagsEndpoint
{
    internal sealed record Response(string Name);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromQuery] string? tagName,
        [AsParameters] PageRequest page,
        [FromServices] IProjectTagQuery tags,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        var filter = new ProjectTagFilter(tagName);
        var queryPage = await tags.GetProjectTagsAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items.Select(t => new Response(t.Name)).ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectTagsEndpoint()
        {
            app.MapGet("ProjectTags", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectTagsV2")
            .WithSummary("List project tags")
            .WithDescription("Returns all project tags, optionally filtered by name.")
            .WithTags("ProjectTag")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK);
    }
}