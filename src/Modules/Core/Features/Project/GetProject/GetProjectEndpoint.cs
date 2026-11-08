using Core.Shared.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Features.Project.GetProject;

internal static class GetProjectEndpoint
{
    internal sealed record Request([FromRoute] ProjectShortName ProjectShortName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IProjectQuery projects,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(request.ProjectShortName) is { } denied)
            return denied;

        var project = await projects.GetProjectAsync(request.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        return Results.Ok(GetProjectResponse.From(project));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectEndpoint()
        {
            app.MapGet("Projects/{projectShortName}", HandleAsync)
                .CacheOutput(ProjectCache.PolicyName)
                .WithETag()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectByShortNameV2")
            .WithSummary("Get project by short name")
            .WithDescription("Returns the project details for the specified short name.")
            .WithTags("Project")
            .Produces<GetProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}