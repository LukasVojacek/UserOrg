using System.Diagnostics.CodeAnalysis;

using Core.Shared.OutputCache;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.Http;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.Project.ChangeProjectUserRole;

/// <summary>
/// Changes a member's project role, enforcing the relative role hierarchy (strictly below the caller's
/// level, no lower than the member baseline) and the single-owner invariant — both in the Project
/// aggregate. Mirrors legacy <c>PUT api/Project/{ProjectShortName}/User/{IdpUserId}/Assign/{RoleId}</c>.
/// </summary>
internal static class ChangeProjectUserRoleEndpoint
{
    internal sealed record Request(
        [FromRoute] ProjectShortName ProjectShortName,
        [FromRoute] string IdpUserId,
        [FromBody] Body Body);

    internal sealed record Body(string RoleName);

    // IAuditLogger<T> uses T only as a logger category, mirroring ILogger<T> — but the endpoint
    // itself is a static class and cannot fill that slot, hence this marker.
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IRoleListService roleListService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(request.ProjectShortName) is { } denied)
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectUserRoleChanged, caller, request.ProjectShortName) with
            {
                TargetType = "user",
                TargetId = request.IdpUserId,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return denied;
        }

        var roles = await roleListService.ListRoles(ct);
        if (!TryResolveRoles(roles, request.Body.RoleName, roleOptions.Value, out var resolved, out var roleProblem))
            return roleProblem;

        // The caller's highest role level in this project, read from the projectRoles claim.
        var requesterLevel = RequesterLevel(caller, request.ProjectShortName, roles.Value);

        var target = await users.FindAsync(new IdpId(request.IdpUserId), ct);
        if (target is null)
            return Problems.NotFound("project.user_not_found", "The target user does not exist.");

        var project = await repository.FindAsync(request.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        var assignment = new ProjectRoleAssignment(
            ProjectRoleId.Create(resolved.Target.Id),
            resolved.Target.Level,
            requesterLevel,
            resolved.Member.Level,
            ProjectRoleId.Create(resolved.Owner.Id));

        var change = project.ChangeUserRole(target, assignment);
        if (!change.IsSuccess)
        {
            await AuditHierarchyRefusalAsync(auditLogger, caller, request.ProjectShortName, target, change.Error);
            return change.Error.ToProblem();
        }

        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [target.IdentityProviderId.Value]);

        var roleChangeData = RoleChangeData(resolved.Target, change.Value, roles.Value);

        await cache.EvictAsync(CacheTags.Project(request.ProjectShortName.Value), ct);
        await cache.EvictAsync(CacheTags.UserInfo(request.IdpUserId), ct);

        // Last, because the audit write is fail-closed: it may throw, and the accepted consequence is a
        // committed change behind a failed response - not a committed change still served from the cache.
        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectUserRoleChanged, caller, request.ProjectShortName, roleChangeData) with
        {
            TargetType = "user",
            TargetId = target.IdentityProviderId.Value,
        });

        return Results.NoContent();
    }

    private sealed record ResolvedRoles(RoleInfo Target, RoleInfo Member, RoleInfo Owner);

    private static bool TryResolveRoles(
        Result<IList<RoleInfo>, Error> roles,
        string requestedRoleName,
        WellKnownRoles roleNames,
        [NotNullWhen(true)] out ResolvedRoles? resolved,
        [NotNullWhen(false)] out IResult? problem)
    {
        resolved = null;
        problem = null;

        if (!roles.IsSuccess)
            problem = Problems.RoleServiceUnavailable();
        else if (roles.Value.FindByName(requestedRoleName) is not { } targetRole)
            problem = Problems.Validation(ProjectErrorCodes.UnknownRole, "Unknown role.");
        else if (roles.Value.FindByName(roleNames.DefaultProjectMemberRoleName) is not { } memberRole
                 || roles.Value.FindByName(roleNames.DefaultProjectOwnerRoleName) is not { } ownerRole)
            problem = Problems.Upstream(CommonErrorCodes.RoleServiceUnavailable, "Project roles are not configured.");
        else
            resolved = new ResolvedRoles(targetRole, memberRole, ownerRole);

        return resolved is not null;
    }

    private static int RequesterLevel(Caller caller, ProjectShortName projectShortName, IList<RoleInfo> roles)
    {
        var callerRoleNames = caller.ProjectRoles
            .Where(kvp => kvp.Value.Contains(projectShortName))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles
            .Where(r => callerRoleNames.Contains(r.Name))
            .Select(r => r.Level)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static async Task AuditHierarchyRefusalAsync(
        IAuditLogger<AuditCategory> auditLogger,
        Caller caller,
        ProjectShortName projectShortName,
        Domain.User.User target,
        Error error)
    {
        if (error.Code != ProjectErrorCodes.CannotAssignEqualOrHigherRole)
            return;

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
            AuditActions.ProjectUserRoleChanged, caller, projectShortName) with
        {
            TargetType = "user",
            TargetId = target.IdentityProviderId.Value,
            Reason = error.Code,
        });
    }

    private static Dictionary<string, object?> RoleChangeData(
        RoleInfo newRole, ProjectRoleId? previousRoleId, IList<RoleInfo> roles)
    {
        var roleChangeData = new Dictionary<string, object?>
        {
            ["new_role_id"] = newRole.Id,
            ["new_role_name"] = newRole.Name,
        };
        // A null previous role is ambiguous by construction: ChangeUserRole never distinguishes a
        // first-time grant to a non-member from a regrant over a soft-deleted role row (Project.cs's
        // `!r.IsDeleted` filter). Omit old_role_* rather than assert a "changed from nothing" that
        // would misrepresent the regrant case in the append-only log.
        if (previousRoleId is { } previous)
        {
            var previousRole = roles.FirstOrDefault(r => r.Id == previous.Value);
            roleChangeData["old_role_id"] = previous.Value;
            if (previousRole is not null)
                roleChangeData["old_role_name"] = previousRole.Name;
        }

        return roleChangeData;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapChangeProjectUserRoleEndpoint()
        {
            app.MapPut("Projects/{projectShortName}/Users/{idpUserId}/Role", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("ChangeProjectUserRoleV2")
            .WithSummary("Change a member's project role")
            .WithDescription("Changes a member's project role; the target role name is supplied in the request body (Body {RoleName}, matched case-insensitively). The caller may only assign a role strictly below their own level and no lower than the member baseline (403 otherwise); a project may hold at most one owner (409). Returns 400 for an unknown role, 409 when the project is read-only.")
            .WithTags("Project")
            .Accepts<Body>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}