using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IUserPermissionRepository : IRepository<UserPermission, Guid>
{

    Task<List<UserPermission>> GetPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<List<UserPermission>> GetPermissionsForUserAsync(string idpUserId, CancellationToken cancellationToken);
    Task<UserPermission> GetSystemPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserPermission> GetPermissionForProjectBySystemRoleAsync(Guid projectId, long systemRoleId, CancellationToken cancellationToken);
}