using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Contracts.Events;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Consumer-side handler for a membership role/access change (ADR-0057/A4, plan §4.4). Updates the
/// project-scoped <see cref="UserPermission"/> (same <see cref="WaldurRoleMap"/> + unknown fallback)
/// and the <see cref="UserProjectAccess"/> for the <c>(project, userName)</c> assignment. Profile
/// fields are NOT rewritten (create-only — D16). Single terminal
/// <see cref="IUserOrgDbContext.SaveChangesAsync"/> (ADR-0057/A3).
/// </summary>
internal sealed class UserRoleChangedHandler(
    IUserOrgDbContext context,
    IRoleListService roleListService,
    ILogger<UserRoleChangedHandler> logger)
    : IWaldurEventHandler<UserRoleChanged>
{
    public async Task<Result<Error>> HandleAsync(UserRoleChanged evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var payload = evt.Payload;

        var project = await context.ProjectsQuery
            .FirstOrDefaultAsync(p => p.ExternalId == payload.ProjectExternalId && p.ServiceName == evt.ServiceName, ct);
        if (project is null)
        {
            return Result<Error>.Failure(Errors.EnsureProjectFailed(
                payload.ProjectExternalId, $"project not found for service '{evt.ServiceName}'."));
        }

        var dbUser = await context.UsersQuery.FirstOrDefaultAsync(u => u.UserName == payload.UserName, ct);
        if (dbUser is null)
        {
            return Result<Error>.Failure(Errors.EnsureUserFailed(
                payload.UserName, $"not found for role change in project '{payload.ProjectExternalId}'."));
        }

        var rolesResult = await roleListService.ListRoles(ct);
        if (!rolesResult.IsSuccess)
        {
            return Result<Error>.Failure(rolesResult.Error);
        }

        var roleName = WaldurRoleMap.ResolveProjectRoleName(
            payload.Role, logger, evt.Instance, payload.UserName, payload.ProjectExternalId);
        var roleId = WaldurRoleMap.RequireRoleId(rolesResult.Value, roleName);

        var permission = await context.UserPermissionsQuery
            .Include(p => p.User)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(p => p.User!.Id == dbUser.Id && p.Project!.Id == project.Id, ct);

        if (permission is null)
        {
            context.Add(new UserPermission
            {
                Id = Guid.CreateVersion7(),
                User = dbUser,
                Project = project,
                SystemRoleId = roleId,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else if (permission.SystemRoleId != roleId)
        {
            permission.SystemRoleId = roleId;
            permission.ModifiedAt = DateTime.UtcNow;
        }

        var access = await context.UserProjectAccessesQuery
            .FirstOrDefaultAsync(a => a.ProjectId == project.Id && a.UserId == dbUser.Id, ct);
        if (access is not null)
        {
            if (access.HasAccess != payload.HasAccess)
            {
                access.HasAccess = payload.HasAccess;
                access.ModifiedAt = DateTime.UtcNow;
            }

            if (access.IsDeleted)
            {
                access.IsDeleted = false;
                access.ModifiedAt = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(ct);
        return Result<Error>.Success();
    }
}