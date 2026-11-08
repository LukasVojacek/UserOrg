using Forge.Core.Features.Role.Services;
using Forge.Core.Features.UserOrgRoles;

using Microsoft.Extensions.Caching.Hybrid;

namespace UserOrg.Gateway.Startup;

/// <summary>
/// Pre-computed snapshot of a user's permissions, cached for 30 seconds to avoid repeated DB calls
/// per request. All downstream consumers in a single request share the same data.
/// </summary>
internal sealed record UserPermissionsSnapshot
{
    public IList<UserPermissionDto> RawPermissions { get; init; } = [];
    public HashSet<string> PermissionKeys { get; init; } = [];
    public IDictionary<string, ICollection<string>> ProjectRecords { get; init; } = new Dictionary<string, ICollection<string>>();
    public string EffectiveRoleClaim { get; init; } = string.Empty;
    public HashSet<string> UserProjectShortNames { get; init; } = [];
}

internal interface IUserPermissionsCache
{
    Task<UserPermissionsSnapshot> GetAsync(string idpUserId, CancellationToken ct);

    Task InvalidateAsync(string idpUserId, CancellationToken ct);

    Task InvalidateAllAsync(CancellationToken ct);
}

/// <summary>
/// Singleton cache for user permissions. Uses <see cref="HybridCache"/> with a 30 s TTL.
/// Resolves scoped <see cref="IUserOrgRolesService"/> and <see cref="IRoleService"/> via
/// <see cref="IServiceScopeFactory"/> so the singleton lifetime does not conflict with scoped services.
/// </summary>
internal sealed class UserPermissionsCache(
    HybridCache hybridCache,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IUserPermissionsCache
{
    private const string Tag = "userperms";
    private static readonly string[] Tags = [Tag];

    private static string KeyOf(string idpUserId) => $"{Tag}:{idpUserId}";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public async Task<UserPermissionsSnapshot> GetAsync(string idpUserId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(idpUserId))
            return new UserPermissionsSnapshot();

        var cacheKey = KeyOf(idpUserId);

        // Use state-based overload to avoid closure allocations on hot path
        var snapshot = await hybridCache.GetOrCreateAsync(
            cacheKey,
            state: (scopeFactory, configuration, idpUserId),
            static async (state, cancel) =>
            {
                var (sf, config, userId) = state;
                await using var scope = sf.CreateAsyncScope();
                var rolesService = scope.ServiceProvider.GetRequiredService<IUserOrgRolesService>();
                var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

                var rawPerms = await rolesService.GetUserPermissionsAsync(userId, null, cancel);
                var permKeys = await BuildPermissionKeysAsync(roleService, rawPerms, cancel);
                IDictionary<string, ICollection<string>> projectRecords = rawPerms.ToProjectRecords(config, out var roleClaim);

                var projectShortNames = rawPerms
                    .Where(p => !string.IsNullOrEmpty(p.ShortName))
                    .Select(p => p.ShortName!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                return new UserPermissionsSnapshot
                {
                    RawPermissions = rawPerms,
                    PermissionKeys = permKeys,
                    ProjectRecords = projectRecords,
                    EffectiveRoleClaim = roleClaim,
                    UserProjectShortNames = projectShortNames
                };
            },
            options: CacheOptions,
            tags: Tags,
            cancellationToken: ct);

        // A brand-new user is provisioned in the database by the upstream API
        // (UserInitializationEndpointFilter on /api/UserInfo) only AFTER this gateway transform
        // has already run. If that first request resolves an empty snapshot, it must NOT persist:
        // a 30 s cached empty snapshot would 403 every subsequent permission-checked request
        // (e.g. /api/User/Preferences) until the entry expired. Evict it so the next request
        // re-evaluates against fresh DB state once the user record exists.
        // Note: a provisioned user always has >= 1 UserPermission (assigned in UserService.CreateUser),
        // so only the transient mid-initialization state is empty here.
        if (snapshot.RawPermissions.Count == 0)
        {
            await hybridCache.RemoveAsync(cacheKey, ct);
        }

        return snapshot;
    }

    public async Task InvalidateAsync(string idpUserId, CancellationToken ct) =>
        await hybridCache.RemoveAsync(KeyOf(idpUserId), ct);

    public async Task InvalidateAllAsync(CancellationToken ct) =>
        await hybridCache.RemoveByTagAsync(Tag, ct);

    private static async Task<HashSet<string>> BuildPermissionKeysAsync(
        IRoleService roleService,
        IList<UserPermissionDto> userRoles,
        CancellationToken ct)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleNames = userRoles
            .Where(r => !string.IsNullOrEmpty(r.SystemRoleName))
            .Select(r => r.SystemRoleName!)
            .Distinct()
            .ToList();

        foreach (var roleName in roleNames)
        {
            var roleResult = await roleService.GetRole(roleName, ct);
            if (!roleResult.IsSuccess || roleResult.Value is null)
                continue;

            foreach (var permission in roleResult.Value.Permissions)
                permissions.Add(permission.Key.Value);
        }

        return permissions;
    }
}