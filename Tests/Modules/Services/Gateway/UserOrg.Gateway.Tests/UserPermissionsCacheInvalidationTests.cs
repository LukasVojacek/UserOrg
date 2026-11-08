using System.Collections.Concurrent;

using Forge.Core.Features.Role.Services;
using Forge.Core.Features.UserOrgRoles;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests;

public sealed class UserPermissionsCacheInvalidationTests
{
    private const string UserA = "idp-user-a";
    private const string UserB = "idp-user-b";

    private sealed class CountingRolesService : IUserOrgRolesService
    {
        internal ConcurrentDictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);

        public Task<IList<UserPermissionDto>> GetUserPermissionsAsync(string identityProviderUserId, Guid? projectId, CancellationToken cancellationToken)
        {
            var read = Reads.AddOrUpdate(identityProviderUserId, 1, (_, count) => count + 1);
            IList<UserPermissionDto> permissions =
            [
                new UserPermissionDto
                {
                    IdentityProviderUserId = identityProviderUserId,
                    ShortName = $"PROJ-{read}",
                    SystemRoleId = 3,
                    SystemRoleName = string.Empty,
                },
            ];
            return Task.FromResult(permissions);
        }

        public Task<HashSet<string>> GetUserPermissionKeysAsync(string identityProviderUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        internal int ReadsOf(string idpUserId) => Reads.GetValueOrDefault(idpUserId);
    }

    private static (UserPermissionsCache Cache, CountingRolesService Roles, ServiceProvider Services) Build()
    {
        var roles = new CountingRolesService();
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddScoped<IUserOrgRolesService>(_ => roles);
        services.AddScoped(_ => Mock.Of<IRoleService>());
        var provider = services.BuildServiceProvider();

        var cache = new UserPermissionsCache(
            provider.GetRequiredService<HybridCache>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build());
        return (cache, roles, provider);
    }

    [Fact]
    public async Task Should_ServeTheCachedSnapshot_When_NothingWasInvalidated()
    {
        var (cache, roles, services) = Build();
        await using var _ = services;
        var ct = TestContext.Current.CancellationToken;

        await cache.GetAsync(UserA, ct);
        var second = await cache.GetAsync(UserA, ct);

        Assert.Equal(1, roles.ReadsOf(UserA));
        Assert.Contains("PROJ-1", second.UserProjectShortNames);
    }

    [Fact]
    public async Task Should_RebuildTheSnapshot_When_TheUserWasInvalidated()
    {
        var (cache, roles, services) = Build();
        await using var _ = services;
        var ct = TestContext.Current.CancellationToken;

        await cache.GetAsync(UserA, ct);
        await cache.InvalidateAsync(UserA, ct);
        var rebuilt = await cache.GetAsync(UserA, ct);

        Assert.Equal(2, roles.ReadsOf(UserA));
        Assert.Contains("PROJ-2", rebuilt.UserProjectShortNames);
        Assert.Contains("PROJ-2", rebuilt.ProjectRecords.Values.SelectMany(projects => projects));
    }

    [Fact]
    public async Task Should_KeepOtherUsersSnapshots_When_OneUserWasInvalidated()
    {
        var (cache, roles, services) = Build();
        await using var _ = services;
        var ct = TestContext.Current.CancellationToken;

        await cache.GetAsync(UserA, ct);
        await cache.GetAsync(UserB, ct);
        await cache.InvalidateAsync(UserA, ct);
        await cache.GetAsync(UserB, ct);

        Assert.Equal(1, roles.ReadsOf(UserB));
    }

    [Fact]
    public async Task Should_RebuildEverySnapshot_When_AllWereInvalidated()
    {
        var (cache, roles, services) = Build();
        await using var _ = services;
        var ct = TestContext.Current.CancellationToken;

        await cache.GetAsync(UserA, ct);
        await cache.GetAsync(UserB, ct);
        await cache.InvalidateAllAsync(ct);
        var rebuiltA = await cache.GetAsync(UserA, ct);
        var rebuiltB = await cache.GetAsync(UserB, ct);

        Assert.Equal(2, roles.ReadsOf(UserA));
        Assert.Equal(2, roles.ReadsOf(UserB));
        Assert.Contains("PROJ-2", rebuiltA.UserProjectShortNames);
        Assert.Contains("PROJ-2", rebuiltB.UserProjectShortNames);
    }

    [Fact]
    public async Task Should_LeaveOtherHybridCacheEntries_When_AllSnapshotsWereInvalidated()
    {
        var (cache, _, services) = Build();
        await using var __ = services;
        var ct = TestContext.Current.CancellationToken;
        var hybridCache = services.GetRequiredService<HybridCache>();
        var otherReads = 0;
        ValueTask<string> Other(CancellationToken _)
        {
            otherReads++;
            return ValueTask.FromResult("value");
        }

        await hybridCache.GetOrCreateAsync("unrelated:key", Other, cancellationToken: ct);
        await cache.GetAsync(UserA, ct);
        await cache.InvalidateAllAsync(ct);
        await hybridCache.GetOrCreateAsync("unrelated:key", Other, cancellationToken: ct);

        Assert.Equal(1, otherReads);
    }
}