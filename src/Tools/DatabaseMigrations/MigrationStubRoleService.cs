using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace UserOrg.Tools.DatabaseMigrations;

/// <summary>
/// No-op <see cref="IRoleService"/> so the modular <c>Core.DataAccess.Contexts.DatabaseContext</c> can be
/// constructed by the migration tool. The role service is never invoked during migration model-building,
/// apply, or script generation — only the context's EF model is needed.
/// </summary>
internal sealed class MigrationStubRoleService : IRoleService
{
    private static NotSupportedException NotSupported() => new("Role service is not available in the migration tool.");

    public Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken) => throw NotSupported();

    public Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken) => throw NotSupported();

    public Task InvalidateRoleListsAsync(CancellationToken cancellationToken) => throw NotSupported();
}