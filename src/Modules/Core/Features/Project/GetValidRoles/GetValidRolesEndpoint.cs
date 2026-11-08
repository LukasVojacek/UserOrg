
using Core.Shared.Paging;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Project.GetValidRoles;

/// <summary>
/// Lists the project roles the caller is permitted to assign to others in the project, based on the role
/// hierarchy: strictly below the caller's own level and no lower than the member baseline. Mirrors legacy
/// <c>GET api/Project/{ProjectShortName}/ValidRoles</c>. Read-only — the caller's project roles come from
/// the internal-token claim, so no database lookup of the caller is needed (ADR-0006).
/// </summary>
internal static class GetValidRolesEndpoint
{
    internal sealed record Response(string Name, string Description, IReadOnlyList<string> Permissions);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromRoute] ProjectShortName projectShortName,
        Caller caller,
        [FromServices] IRoleListService roleListService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(projectShortName) is { } denied)
            return denied;

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
            return Problems.RoleServiceUnavailable();

        // Role names the caller holds in this project, read from the projectRoles claim.
        var callerRoleNames = caller.ProjectRoles
            .Where(kvp => kvp.Value.Contains(projectShortName))
            .Select(kvp => kvp.Key)
            .ToList();

        var response = ValidRolesFor(roles.Value, callerRoleNames, roleOptions.Value.DefaultProjectMemberRoleName)
            .Select(r => new Response(r.Name, r.Description, r.Permissions.Select(p => p.Key.Value).ToList()))
            .ToList();

        return PagedResponse.Complete(http, response);
    }

    /// <summary>
    /// Roles strictly below the caller's highest project-role level and at/above the member baseline.
    /// Empty when the caller holds no role in the project.
    /// </summary>
    private static IEnumerable<RoleInfo> ValidRolesFor(
        IList<RoleInfo> allRoles, IReadOnlyCollection<string> callerRoleNames, string memberRoleName)
    {
        var callerRoles = allRoles
            .Where(r => callerRoleNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (callerRoles.Count == 0)
            return [];

        var highest = callerRoles.MaxBy(r => r.Level)!;
        var memberRole = allRoles.FindByName(memberRoleName);

        return allRoles.Where(r =>
            highest.IsHigherLevelThan(r)
            && (memberRole is null || !memberRole.IsHigherLevelThan(r)));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetValidRolesEndpoint()
        {
            app.MapGet("Projects/{projectShortName}/ValidRoles", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetValidRolesV2")
            .WithSummary("List assignable project roles for the caller")
            .WithDescription("Returns the project roles the caller may assign to other members of the project (strictly below the caller's own level, no lower than the member baseline) as a single complete page (bounded reference list — paging parameters are ignored).")
            .WithTags("Project")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}