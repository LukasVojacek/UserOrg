using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Forge.Core.Features.UserOrgRoles;

/// <summary>
/// <see cref="HybridCache"/>-backed provider id → name resolver (30 s TTL, the same shape as the
/// Gateway's permissions cache). The empty string is used as the cached "not found" sentinel so the
/// cache never has to store null.
/// </summary>
internal sealed class ProviderIdentityResolver(IDbContextFactory<UserOrgDbContext> dbContextFactory, HybridCache cache) : IProviderIdentityResolver
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public async Task<string?> GetProviderNameAsync(Guid providerId, CancellationToken cancellationToken)
    {
        if (providerId == Guid.Empty)
            return null;

        var name = await cache.GetOrCreateAsync(
            $"providername:{providerId}",
            state: (dbContextFactory, providerId),
            static async (state, ct) =>
            {
                await using var dbContext = await state.dbContextFactory.CreateDbContextAsync(ct);
                return await dbContext.GetProviderNameByIdAsync(state.providerId, ct) ?? string.Empty;
            },
            options: CacheOptions,
            tags: null,
            cancellationToken: cancellationToken);

        return string.IsNullOrEmpty(name) ? null : name;
    }

    public async Task<string?> GetAssignedProviderNameAsync(string idpUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(idpUserId))
            return null;

        var name = await cache.GetOrCreateAsync(
            $"assignedprovidername:{idpUserId}",
            state: (dbContextFactory, idpUserId),
            static async (state, ct) =>
            {
                await using var dbContext = await state.dbContextFactory.CreateDbContextAsync(ct);
                return await dbContext.GetAssignedProviderNameByIdpUserIdAsync(state.idpUserId, ct) ?? string.Empty;
            },
            options: CacheOptions,
            tags: null,
            cancellationToken: cancellationToken);

        return string.IsNullOrEmpty(name) ? null : name;
    }
}