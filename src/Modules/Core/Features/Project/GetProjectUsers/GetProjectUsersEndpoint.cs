using Core.Shared.Paging;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Project.GetProjectUsers;

internal static class GetProjectUsersEndpoint
{
    internal sealed record Response(string IdpUserId, string Email, string UserName, string RoleName);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromRoute] ProjectShortName projectShortName,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IProjectQuery projects,
        [FromServices] IRoleListService roleListService,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        if (caller.RequireProjectScope(projectShortName) is { } denied)
            return denied;

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
            return Problems.RoleServiceUnavailable();

        var queryPage = await projects.GetProjectUsersAsync(projectShortName, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(u => new Response(
                u.IdentityProviderId.Value,
                u.Email.Value,
                u.UserName.Value,
                roles.Value.FirstOrDefault(r => r.Id == u.RoleId.Value)?.Name ?? string.Empty))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectUsersEndpoint()
        {
            app.MapGet("Projects/{projectShortName}/Users", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectUsersV2")
            .WithSummary("List users assigned to a project")
            .WithDescription("Returns active users assigned to the specified project, with their project role, as a paged list (?page=&limit=). Errors carry a machine-readable code in the problem extensions — branch on that, not the message.")
            .WithTags("Project")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}