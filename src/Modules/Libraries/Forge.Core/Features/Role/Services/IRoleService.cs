using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Forge.Core.Features.Role.Services;

/// <summary>
/// Full role management service.
/// Includes all role operations: create, read, update, and permission management.
/// Inherits from IRoleListService for read-only scenarios.
/// </summary>
public interface IRoleService : IRoleListService
{
    Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken);
    Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken);
    Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken);
    Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken);
    Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken);
    Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken);
    Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken);
    Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken);
    Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken);

    /// <summary>
    /// Discards cached role lists so the next <see cref="IRoleListService.ListRoles"/> or
    /// <see cref="ListRoleNames"/> rebuilds from the database. Required for callers that write
    /// roles without going through this service; which cache tags back those lists stays an
    /// implementation detail.
    /// </summary>
    Task InvalidateRoleListsAsync(CancellationToken cancellationToken);
}