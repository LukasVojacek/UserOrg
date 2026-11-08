using Microsoft.Extensions.Caching.Hybrid;

using UserOrganizationManagement.Application.UserOrg.IServices;

namespace UserOrganizationManagement.API.Authentication;

internal interface IProviderIdResolver
{
    /// <summary>
    /// Returns the provider ID for the given IDP user ID, cached for 60 seconds.
    /// Returns null when the user is not linked to a provider.
    /// </summary>
    Task<Guid?> GetProviderIdAsync(string idpUserId, CancellationToken ct);
}

/// <summary>
/// Singleton cache for provider ID lookups that occur inside JWT <c>OnTokenValidated</c>.
/// Uses <see cref="HybridCache"/> with a 60 s TTL to avoid a DB round-trip on every
/// ProviderAdministrator request.
/// </summary>
internal sealed class ProviderIdResolver(
    HybridCache hybridCache,
    IServiceScopeFactory scopeFactory) : IProviderIdResolver
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(60)
    };

    public Task<Guid?> GetProviderIdAsync(string idpUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idpUserId))
            return Task.FromResult<Guid?>(null);

        // Cache the result as a string; empty string = no provider linked.
        var cacheKey = $"providerid:{idpUserId}";
        return GetOrCreateNullableGuidAsync(cacheKey, idpUserId, ct);
    }

    private async Task<Guid?> GetOrCreateNullableGuidAsync(string cacheKey, string idpUserId, CancellationToken ct)
    {
        // Cache as string because HybridCache handles null-return from factory inconsistently.
        // Empty string = null provider.
        var cached = await hybridCache.GetOrCreateAsync(
            cacheKey,
            state: (scopeFactory, idpUserId),
            static async (state, cancel) =>
            {
                var (sf, userId) = state;
                await using var scope = sf.CreateAsyncScope();
                var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                var id = await userService.GetProviderIdByIdpUserIdAsync(userId, cancel);
                return id?.ToString() ?? string.Empty;
            },
            options: CacheOptions,
            tags: null,
            cancellationToken: ct);

        if (string.IsNullOrEmpty(cached))
            return null;

        return Guid.TryParse(cached, out var guid) ? guid : null;
    }
}