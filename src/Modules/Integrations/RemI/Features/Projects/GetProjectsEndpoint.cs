using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using RemI.HostContracts;

namespace RemI.Features.Projects;

/// <summary>
/// <c>GET /api/remi/projects</c> for the RemI service. Every project, unfiltered and unscoped: the caller is
/// a service identity carrying <c>perm:service:remi</c>, gated at the Gateway, not per project role.
/// </summary>
public static class GetProjectsEndpoint
{
    internal static async Task<IResult> HandleAsync(
        [FromServices] IUserorgService userorgService,
        CancellationToken ct)
    {
        var result = await userorgService.GetProjectsAsync(ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
    }

    extension(IEndpointRouteBuilder app)
    {
        public IEndpointRouteBuilder MapGetProjectsEndpoint()
        {
            app.MapGet("projects", HandleAsync)
                .MapDocumentation();

            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjects")
            .WithSummary("Get projects")
            .WithDescription("Returns a list of projects")
            .WithTags("RemI")
            .Produces<IReadOnlyList<ProjectResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}