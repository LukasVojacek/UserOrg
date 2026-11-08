using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Forge.Core.Features.Role.Services;

/// <summary>
/// Read-only service for listing roles.
/// Use this interface when you only need to read role information.
/// </summary>
public interface IRoleListService
{
    Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken);
}