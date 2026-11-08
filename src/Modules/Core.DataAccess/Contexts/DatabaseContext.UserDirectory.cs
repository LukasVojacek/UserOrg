using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

/// <summary>
/// <see cref="IUserDirectory"/> implementation on DatabaseContext. Projects the surrogate
/// <see cref="UserId"/> from an <see cref="IdpId"/> for the write path (audit stamping), without
/// hydrating the full User aggregate or invoking the role service.
/// </summary>
internal sealed partial class DatabaseContext : IUserDirectory
{
    async Task<UserId?> IUserDirectory.FindUserIdAsync(IdpId idpId, CancellationToken cancellationToken) =>
        await Users
            .Where(u => u.IdentityProviderId == idpId)
            .Select(u => (UserId?)EF.Property<UserId>(u, "Id"))
            .FirstOrDefaultAsync(cancellationToken);

    async Task<IdpId?> IUserDirectory.FindIdpIdAsync(UserId userId, CancellationToken cancellationToken) =>
        await Users
            .Where(u => EF.Property<UserId>(u, "Id") == userId)
            .Select(u => (IdpId?)u.IdentityProviderId)
            .FirstOrDefaultAsync(cancellationToken);
}