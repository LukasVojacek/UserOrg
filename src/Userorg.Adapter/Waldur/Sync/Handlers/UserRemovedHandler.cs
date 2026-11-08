using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Application.Abstraction;

using Waldur.Contracts.Events;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Consumer-side handler for a membership removal (ADR-0057/A4, ADR-0054/A2, plan §4.4). TRIPLE
/// soft-delete: marks the <c>AssignedUserToProject</c> row, the <c>UserProjectAccess</c> row(s) AND
/// the project-scoped <c>UserPermission</c> row deleted for the <c>(project, userName)</c> pair — so a
/// removed user loses their effective project role. The system-level PlatformUser
/// <c>UserPermission</c> (<c>Project == null</c>) is intentionally PRESERVED (platform identity).
/// Replay against an already-absent (or already-deleted) assignment is a required no-op (idempotent).
/// Single terminal <see cref="IUserOrgDbContext.SaveChangesAsync"/>, and only when something actually
/// changed.
/// </summary>
internal sealed class UserRemovedHandler(IUserOrgDbContext context) : IWaldurEventHandler<UserRemoved>
{
    public async Task<Result<Error>> HandleAsync(UserRemoved evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var project = await context.ProjectsQuery
            .FirstOrDefaultAsync(p => p.ExternalId == evt.ProjectExternalId && p.ServiceName == evt.ServiceName, ct);
        if (project is null)
        {
            return Result<Error>.Success(); // nothing to remove — idempotent no-op
        }

        var dbUser = await context.UsersQuery.FirstOrDefaultAsync(u => u.UserName == evt.UserName, ct);
        if (dbUser is null)
        {
            return Result<Error>.Success(); // member never materialised — idempotent no-op
        }

        var mutated = false;

        var assignment = await context.AssignedUserToProjectsQuery
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.ProjectId == project.Id && a.User!.Id == dbUser.Id && !a.IsDeleted, ct);
        if (assignment is not null)
        {
            assignment.IsDeleted = true;
            assignment.ModifiedAt = DateTime.UtcNow;
            mutated = true;
        }

        var accesses = await context.UserProjectAccessesQuery
            .Where(a => a.ProjectId == project.Id && a.UserId == dbUser.Id && !a.IsDeleted)
            .ToListAsync(ct);
        foreach (var access in accesses)
        {
            access.IsDeleted = true;
            access.ModifiedAt = DateTime.UtcNow;
            mutated = true;
        }

        // Project-scoped permission mirroring UserAssignedHandler.EnsureProjectPermissionAsync's write
        // key. The system-level PlatformUser permission (Project == null) is deliberately left intact —
        // removal from one project must not strip platform identity.
        var permission = await context.UserPermissionsQuery
            .Include(p => p.User)
            .Include(p => p.Project)
            .FirstOrDefaultAsync(p => p.Project!.Id == project.Id && p.User!.Id == dbUser.Id && !p.IsDeleted, ct);
        if (permission is not null)
        {
            permission.IsDeleted = true;
            permission.ModifiedAt = DateTime.UtcNow;
            mutated = true;
        }

        if (mutated)
        {
            await context.SaveChangesAsync(ct);
        }

        return Result<Error>.Success();
    }
}