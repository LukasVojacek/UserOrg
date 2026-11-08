using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Core.UnitTests;

public sealed partial class UserDatabaseAccessTests
{
    internal class TestRoleService : IRoleService
    {
        public Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyDictionary<int, string>, Error>.Success(IntegrationDb.WellKnownRoleNames));
        }

        public Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task InvalidateRoleListsAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
