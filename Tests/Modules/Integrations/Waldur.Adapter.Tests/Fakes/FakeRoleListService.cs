using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Configurable test double for <see cref="IRoleListService"/>.
/// Returns the role list supplied via the constructor; defaults to an empty list.
/// </summary>
internal sealed class FakeRoleListService : IRoleListService
{
    private readonly IList<RoleInfo> _roles;

    public FakeRoleListService(IList<RoleInfo>? roles = null)
        => _roles = roles ?? [];

    public Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken)
        => Task.FromResult(Result<IList<RoleInfo>, Error>.Success(_roles));
}
