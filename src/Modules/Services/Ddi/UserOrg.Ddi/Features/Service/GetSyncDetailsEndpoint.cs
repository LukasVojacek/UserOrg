using System.Security.Claims;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace UserOrg.Ddi.Features.Service;

/// <summary>
/// <c>GET /api/v2/Service/SyncDetails</c> — every user the calling provider should synchronise, each with the
/// project-scoped roles to emit. Reproduces the legacy <c>List&lt;UserInfoExtendedModel&gt;</c> wire shape
/// (ADR-0042) over the <see cref="ISyncDetailsQuery"/> read query: the query is a pure DB projection (role
/// <b>ids</b> only); the endpoint resolves role names + permissions via the Forge.Core
/// <see cref="IRoleListService"/>.
/// </summary>
internal static class GetSyncDetailsEndpoint
{
    internal sealed record Response(
        Guid Id,
        string? KeycloakSid,
        string? Name,
        string? UserName,
        string? Email,
        bool IsDeleted,
        IReadOnlyList<SystemRoleModel> SystemRoles);

    internal sealed record SystemRoleModel(
        string ProjectShortName,
        string UserEmail,
        string RoleName,
        IReadOnlyList<ProjectResourceModel> ProjectResources,
        IReadOnlyList<string> SystemPermissionTypes);

    internal sealed record ProjectResourceModel(
        string Name,
        string Description,
        DateTime? StartDate,
        DateTime? EndDate,
        EProjectResourceState State,
        IReadOnlyList<LocationModel> Locations);

    // Legacy LocationDetailModel misspells "Description" as "Descritpion" — preserve the wire key for parity.
    internal sealed record LocationModel(
        string Name,
        string Descritpion,
        string Endpoint,
        bool IsPrivate,
        string Type,
        long? AllocationUnitCount,
        EAllocationUnitType AllocationUnitType,
        IReadOnlyList<KeyValuePair<string, string>> Values);

    internal static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISyncDetailsQuery query,
        IRoleListService roleListService,
        CancellationToken ct)
    {
        var providerIdValue = user.FindFirstValue(AuthConstants.ProviderIdClaimType);
        if (!Guid.TryParse(providerIdValue, out var providerId))
        {
            // Parity with legacy ServiceEndpoint: a missing/invalid provider id is a 400.
            return Results.BadRequest("Provider id not found");
        }

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            return Results.Problem(
                title: "Failed to retrieve roles",
                detail: roles.Error.Description,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // role id → (name, permission keys). One pass over the role list.
        var roleById = roles.Value.ToDictionary(
            r => r.Id,
            r => (r.Name, Permissions: (IReadOnlyList<string>)r.Permissions.Select(p => p.Key.Value).ToList()));

        var views = await query.GetSyncDetailsAsync(providerId, ct);
        var response = views.Select(v => MapUser(v, roleById)).ToList();
        return Results.Ok(response);
    }

    private static Response MapUser(
        SyncUserView v,
        IReadOnlyDictionary<int, (string Name, IReadOnlyList<string> Permissions)> roleById) => new(
            v.Id,
            v.KeycloakSid,
            null,
            v.UserName,
            v.Email,
            v.IsDeleted,
            v.Roles
                .Where(r => roleById.ContainsKey(r.SystemRoleId))
                .Select(r => MapRole(r, roleById[r.SystemRoleId]))
                .ToList());

    private static SystemRoleModel MapRole(
        SyncUserRoleView r,
        (string Name, IReadOnlyList<string> Permissions) role) => new(
            r.ProjectShortName,
            r.UserEmail,
            role.Name,
            r.ProjectResources.Select(MapResource).ToList(),
            role.Permissions);

    private static ProjectResourceModel MapResource(SyncResourceView r) => new(
        r.Name,
        r.Description ?? string.Empty,
        r.StartDate?.UtcDateTime,
        r.EndDate?.UtcDateTime,
        r.State,
        r.Locations.Select(MapLocation).ToList());

    private static LocationModel MapLocation(SyncLocationView l) => new(
        l.Name,
        l.Description ?? string.Empty,
        l.Endpoint,
        l.IsPrivate,
        l.Type,
        l.AllocationUnitCount,
        l.AllocationUnitType,
        l.Values.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)).ToList());

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetSyncDetailsEndpoint()
        {
            app.MapGet("SyncDetails", HandleAsync).MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetSyncDetails")
            .WithSummary("Synchronisation details for a LEXIS Provider: users and their project roles.")
            .WithTags("Service")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }
}