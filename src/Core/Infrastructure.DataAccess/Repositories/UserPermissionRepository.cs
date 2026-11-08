using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class UserPermissionRepository(DatabaseContext context) : GenericRepository<UserPermission, Guid>(context), IUserPermissionRepository
{
    public async Task<List<UserPermission>> GetPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken) => await Context.UserPermissions
            .Where(x => x.User.Id == userId)
            .ToListAsync(cancellationToken)
            ;
    public async Task<List<UserPermission>> GetPermissionsForUserAsync(string idpUserId, CancellationToken cancellationToken) => await Context.UserPermissions
            .Include(x => x.Project)
            .Where(x => x.User.IdentityProviderUserId == idpUserId)
            .ToListAsync(cancellationToken)
            ;

    public async Task<UserPermission> GetSystemPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken) => await Context.UserPermissions
            .Where(x => x.User.Id == userId && x.Project == null)
            .FirstAsync(cancellationToken)
            ;

    public async Task<UserPermission> GetPermissionForProjectBySystemRoleAsync(Guid projectId, long systemRoleId, CancellationToken cancellationToken) => await Context.UserPermissions
        .Include(x => x.User)
        .Where(x => x.Project.Id == projectId && x.SystemRoleId == systemRoleId && !x.IsDeleted && !x.User.IsDeleted)
        .FirstOrDefaultAsync(cancellationToken)
        ;
}