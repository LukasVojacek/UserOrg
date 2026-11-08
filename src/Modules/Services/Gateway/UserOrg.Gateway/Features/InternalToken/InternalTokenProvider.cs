using System.Security.Claims;
using System.Text;

using Microsoft.Extensions.Caching.Hybrid;

using UserOrg.Shared;

namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Default implementation of <see cref="IInternalTokenProvider"/>.
/// Wraps <see cref="TokenService"/> with a <see cref="HybridCache"/>-backed token cache so that
/// repeated requests from the same user within <see cref="InternalTokenOptions.TokenCacheSeconds"/>
/// seconds reuse the same signed JWT instead of paying ECDSA signing on every request.
/// </summary>
internal sealed class InternalTokenProvider(
    KeyService keyService,
    TokenService tokenService,
    HybridCache hybridCache,
    InternalTokenOptions options) : IInternalTokenProvider
{
    private readonly KeyService _keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));
    private readonly TokenService _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));

    private HybridCacheEntryOptions BuildCacheOptions()
    {
        // Safety margin: expire cached token 60 s before the actual JWT exp.
        var ttl = TimeSpan.FromSeconds(
            Math.Max(1, Math.Min(options.TokenCacheSeconds, options.ExpirationMinutes * 60 - 60)));
        return new HybridCacheEntryOptions
        {
            Expiration = ttl,
            LocalCacheExpiration = ttl
        };
    }

    public Task<string?> GenerateInternalTokenAsync(
        EAuthorizationMethod authMethod,
        string subject,
        string username,
        string systemRole,
        string? providerId = null,
        string? email = null,
        IDictionary<string, ICollection<string>>? projectRoles = null,
        string? providerName = null,
        string? originalJti = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(systemRole);

        var projectHash = ComputeProjectHash(projectRoles);

        // originalJti is a key segment, not just a payload: without it a cached token would keep
        // relaying the previous IdP token's id after a refresh inside the cache TTL. providerName is
        // not a key segment and is a DB lookup, not a pure function of the ones that are: reassigning
        // an interactive ProviderAdmin (D5), or renaming a provider on the service-key path, leaves
        // every segment identical, so the entry keeps serving the old provider_name until it expires.
        // Its staleness is therefore bounded by the resolver's own 30 s TTL plus the remaining life of
        // this entry (TokenCacheSeconds, default 30) — worse than systemRole and projectHash, which are
        // key segments and so change the key as soon as the permissions cache refreshes. Both are within
        // ADR-0011's eventual-consistency rider, which bounds staleness by the Gateway's cache TTLs.
        var cacheKey = $"internaltoken:{authMethod}:{subject}:{systemRole}:{providerId ?? string.Empty}:{projectHash}:{originalJti ?? string.Empty}";

        return hybridCache.GetOrCreateAsync(
            cacheKey,
            state: (_tokenService, authMethod, subject, username, systemRole, provider: (providerId, providerName), email, projectRoles, originalJti),
            static async (state, cancel) =>
            {
                var (svc, am, sub, uname, role, provider, mail, roles, jti) = state;
                return await svc.GenerateTokenAsync(am, sub, uname, provider.providerId ?? string.Empty, role, mail, roles, provider.providerName, jti);
            },
            options: BuildCacheOptions(),
            tags: null,
            cancellationToken: default).AsTask();
    }

    public Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience = "forge")
    {
        ArgumentException.ThrowIfNullOrEmpty(originService);
        ArgumentException.ThrowIfNullOrEmpty(targetAudience);

        var cacheKey = $"internaltoken:s2s:{originService}:{targetAudience}";

        return hybridCache.GetOrCreateAsync(
            cacheKey,
            state: (_tokenService, originService, targetAudience),
            static async (state, cancel) =>
            {
                var (svc, origin, audience) = state;
                return await svc.GenerateServiceToServiceTokenAsync(origin, audience);
            },
            options: BuildCacheOptions(),
            tags: null,
            cancellationToken: default).AsTask();
    }

    public Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token)
        => _tokenService.ValidateTokenAsync(token);

    public string GetCurrentKeyId()
        => _keyService.GetCurrentKeyId();

    private static string ComputeProjectHash(IDictionary<string, ICollection<string>>? projectRoles)
    {
        if (projectRoles is null || projectRoles.Count == 0)
            return "empty";

        var sb = new StringBuilder();
        foreach (var kvp in projectRoles.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append(kvp.Key).Append(':');
            sb.Append(string.Join(',', kvp.Value.OrderBy(s => s, StringComparer.Ordinal)));
            sb.Append(';');
        }

        // FNV-1a 32-bit hash — stable, fast, no heap allocation
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var h = 2166136261u;
        foreach (var b in bytes)
            h = (h ^ b) * 16777619u;
        return h.ToString("x8");
    }
}