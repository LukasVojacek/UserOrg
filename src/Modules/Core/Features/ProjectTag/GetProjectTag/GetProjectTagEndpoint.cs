using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.ProjectTag.GetProjectTag;

internal static class GetProjectTagEndpoint
{
    internal sealed record Request([FromRoute] string TagName);

    internal sealed record Response(string Name);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IProjectTagQuery tags,
        CancellationToken ct)
    {
        // A tag is addressed by its natural key (name, globally unique), not a surrogate GUID (ADR-0022).
        if (!Core.Domain.Project.TagName.TryCreate(request.TagName, out var name, out _))
            return Problems.Validation("projecttag.invalid_name", "Project tag name is invalid.");

        var result = await tags.GetProjectTagByNameAsync(name, ct);
        if (result is null)
            return Problems.NotFound("projecttag.not_found", "Project tag not found.");

        return Results.Ok(new Response(result.Name));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectTagEndpoint()
        {
            app.MapGet("ProjectTags/{tagName}", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectTagByNameV2")
            .WithSummary("Get project tag by name")
            .WithDescription("Returns a single project tag by its globally unique name.")
            .WithTags("ProjectTag")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}