using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Forge.Core.Features.Permission.Services;

public interface IPermissionService
{
    Task<Result<PermissionInfo, Error>> CreatePermission(PermissionKey key, string description, CancellationToken cancellationToken);
    Task<Result<Error>> DeletePermission(PermissionKey key, CancellationToken cancellationToken);
    Task<Result<PermissionInfo, Error>> GetPermission(PermissionKey key, CancellationToken cancellationToken);
    Task<Result<IList<PermissionInfo>, Error>> ListPermissions(CancellationToken cancellationToken);
    Task<Result<bool, Error>> IsPermissionAssignedToRoles(PermissionKey key, CancellationToken cancellationToken);
}