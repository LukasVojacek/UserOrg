using Lukdrasil.Error;
using Lukdrasil.Result;

using RemI.Features.Projects;
using RemI.Features.Users;

namespace RemI.HostContracts;

/// <summary>
/// The port through which the RemI module reads userorg data. The module owns the wire shape;
/// the host (<c>Userorg.Adapter</c>) owns where the rows come from, so this assembly stays free
/// of <c>Core</c> and <c>Core.DataAccess</c>.
/// </summary>
public interface IUserorgService
{
    Task<Result<IReadOnlyList<UserResponse>, Error>> GetUsersAsync(CancellationToken ct);

    Task<Result<UserSystemRolesResponse, Error>> GetUserSystemRoles(string idpUserId, CancellationToken ct);

    Task<Result<IReadOnlyList<ProjectResponse>, Error>> GetProjectsAsync(CancellationToken ct);
}