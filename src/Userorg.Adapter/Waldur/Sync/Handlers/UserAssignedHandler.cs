using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base;
using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Consumer-side handler for a project membership assignment (ADR-0057/A4, plan §4.4). Writes/ensures
/// the FIVE row kinds a Waldur member requires, porting the behaviour of the old
/// <c>WaldurEfpExporter.CreateUserProjectAssignmentEntities</c> + orchestrator
/// <c>EnsureUsersAsync</c> loop:
/// <list type="number">
/// <item><see cref="User"/> — upsert by <c>UserName</c>, profile fields (Email) written on CREATE
/// ONLY (D16 / ADR-0055/A3);</item>
/// <item><see cref="AssignedUserToProject"/> — the project membership;</item>
/// <item>project-scoped <see cref="UserPermission"/> — role via <see cref="WaldurRoleMap"/> (unknown
/// mapped role → <c>Member</c> + warning);</item>
/// <item>system-level <see cref="UserPermission"/> — the <c>PlatformUser</c> ("User") role;</item>
/// <item><see cref="UserProjectAccess"/> — read-only project access per <c>HasAccess</c>.</item>
/// </list>
/// The service account is LOOKED UP (never ensured — B06/F1); the project is resolved SCOPED to
/// <c>(ProjectExternalId, ServiceName)</c> (B07/2a). Single terminal
/// <see cref="IUserOrgDbContext.SaveChangesAsync"/> (ADR-0057/A3): a throw commits nothing and the
/// event replays.
/// </summary>
internal sealed class UserAssignedHandler(
    IUserOrgDbContext context,
    IRoleListService roleListService,
    ILogger<UserAssignedHandler> logger)
    : IWaldurEventHandler<UserAssigned>
{
    public async Task<Result<Error>> HandleAsync(UserAssigned evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var payload = evt.Payload;

        var accountResult = await context.RequireServiceAccountAsync(evt.Instance, evt.ServiceName, ct);
        if (!accountResult.IsSuccess)
        {
            return Result<Error>.Failure(accountResult.Error);
        }

        var serviceAccount = accountResult.Value;

        var project = await context.ProjectsQuery
            .Include(p => p.AssignedUsers)
            .ThenInclude(a => a.User)
            .FirstOrDefaultAsync(p => p.ExternalId == payload.ProjectExternalId && p.ServiceName == evt.ServiceName, ct);
        if (project is null)
        {
            return Result<Error>.Failure(Errors.EnsureProjectFailed(
                payload.ProjectExternalId, $"project not found for service '{evt.ServiceName}'."));
        }

        var rolesResult = await roleListService.ListRoles(ct);
        if (!rolesResult.IsSuccess)
        {
            return Result<Error>.Failure(rolesResult.Error);
        }

        var roles = rolesResult.Value;

        var dbUser = await EnsureUserCreateOnlyAsync(payload, ct);
        EnsureAssignment(project, dbUser, serviceAccount);
        await EnsureProjectPermissionAsync(project, dbUser, roles, evt, ct);
        await EnsureSystemPermissionAsync(dbUser, roles, ct);
        await EnsureProjectAccessAsync(project, dbUser, serviceAccount, payload, evt.ServiceName, ct);

        await context.SaveChangesAsync(ct);
        return Result<Error>.Success();
    }

    /// <summary>Upsert by <c>UserName</c>; profile fields written on CREATE only (D16).</summary>
    private async Task<User> EnsureUserCreateOnlyAsync(ProjectUserPayload payload, CancellationToken ct)
    {
        var existing = await context.UsersQuery.FirstOrDefaultAsync(u => u.UserName == payload.UserName, ct);
        if (existing is not null)
        {
            return existing;
        }

        var toInsert = new User
        {
            Id = Guid.CreateVersion7(),
            IdentityProviderUserId = payload.UserName,
            Email = payload.Email ?? string.Empty,
            UserName = payload.UserName,
            CreatedAt = payload.CreatedAt ?? DateTime.UtcNow,
        };

        context.Add(toInsert);
        return toInsert;
    }

    private void EnsureAssignment(Project project, User dbUser, User serviceAccount)
    {
        var existing = project.AssignedUsers.FirstOrDefault(a => a.User?.Id == dbUser.Id);
        if (existing is null)
        {
            context.Add(new AssignedUserToProject
            {
                ProjectId = project.Id,
                Project = project,
                User = dbUser,
                AssignedByUser = serviceAccount,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            });
            return;
        }

        if (existing.IsDeleted)
        {
            existing.IsDeleted = false;
            existing.ModifiedAt = DateTime.UtcNow;
        }
    }

    private async Task EnsureProjectPermissionAsync(
        Project project,
        User dbUser,
        IList<RoleInfo> roles,
        UserAssigned evt,
        CancellationToken ct)
    {
        var roleName = WaldurRoleMap.ResolveProjectRoleName(
            evt.Payload.Role, logger, evt.Instance, evt.Payload.UserName, evt.Payload.ProjectExternalId);
        var roleId = WaldurRoleMap.RequireRoleId(roles, roleName);

        var existing = await context.UserPermissionsQuery
            .Include(p => p.User)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(p => p.User!.Id == dbUser.Id && p.Project!.Id == project.Id, ct);

        if (existing is null)
        {
            context.Add(new UserPermission
            {
                Id = Guid.CreateVersion7(),
                User = dbUser,
                Project = project,
                SystemRoleId = roleId,
                CreatedAt = DateTime.UtcNow,
            });
            return;
        }

        if (existing.SystemRoleId != roleId)
        {
            existing.SystemRoleId = roleId;
            existing.ModifiedAt = DateTime.UtcNow;
        }

        if (existing.IsDeleted)
        {
            existing.IsDeleted = false;
            existing.ModifiedAt = DateTime.UtcNow;
        }
    }

    private async Task EnsureSystemPermissionAsync(User dbUser, IList<RoleInfo> roles, CancellationToken ct)
    {
        var existing = await context.UserPermissionsQuery
            .Include(p => p.User)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(p => p.User!.Id == dbUser.Id && p.Project == null, ct);

        if (existing is not null)
        {
            return;
        }

        context.Add(new UserPermission
        {
            Id = Guid.CreateVersion7(),
            User = dbUser,
            SystemRoleId = WaldurRoleMap.RequireRoleId(roles, RoleInfoConstants.PlatformUserRoleName),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private async Task EnsureProjectAccessAsync(
        Project project,
        User dbUser,
        User serviceAccount,
        ProjectUserPayload payload,
        string serviceName,
        CancellationToken ct)
    {
        var existing = await context.UserProjectAccessesQuery
            .FirstOrDefaultAsync(a => a.ProjectId == project.Id && a.UserId == dbUser.Id, ct);

        if (existing is null)
        {
            context.Add(new UserProjectAccess
            {
                Id = Guid.CreateVersion7(),
                ProjectId = project.Id,
                UserId = dbUser.Id,
                HasAccess = payload.HasAccess,
                IsDeleted = false,
                AssignedByUser = serviceAccount,
                CreatedAt = DateTime.UtcNow,
                Mode = EOperationMode.ReadOnly,
                ServiceName = serviceName,
            });
            return;
        }

        if (existing.HasAccess != payload.HasAccess)
        {
            existing.HasAccess = payload.HasAccess;
            existing.ModifiedAt = DateTime.UtcNow;
        }

        if (existing.IsDeleted)
        {
            existing.IsDeleted = false;
            existing.ModifiedAt = DateTime.UtcNow;
        }
    }
}