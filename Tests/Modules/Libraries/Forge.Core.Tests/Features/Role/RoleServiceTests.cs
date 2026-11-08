using Forge.Core.Features.Role.Services;
using Forge.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Shared;

namespace Forge.Core.Tests.Features.Role;

/// <summary>
/// Characterization tests pinning today's cache behaviour of <see cref="RoleService"/> — every
/// mutation evicts both the <see cref="CacheTags.RoleList"/> and <see cref="CacheTags.RoleNames"/>
/// tags, and <see cref="RoleService.ListRoles"/>/<see cref="RoleService.ListRoleNames"/> serve
/// from the tagged cache in between.
/// </summary>
public sealed class RoleServiceTests : IDisposable
{
    private readonly ForgeDbContext _context;
    private readonly IDbContextFactory<ForgeDbContext> _contextFactory;
    private readonly HybridCache _cache;
    private readonly RoleService _service;

    public RoleServiceTests()
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ForgeDbContext(options);
        _contextFactory = new TestDbContextFactory(options);
        _cache = new ServiceCollection()
            .AddHybridCache()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<HybridCache>();

        _service = new RoleService(_contextFactory, _cache, NullLogger<RoleService>.Instance);
    }

    [Fact(DisplayName = "ListRoles serves from cache on repeated reads")]
    public async Task ListRoles_ServesFromCache_OnRepeatedReads()
    {
        _context.Roles.Add(new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "viewer",
            Description = "Viewer",
            Category = "Default",
            IsActive = true,
            Level = 1
        });
        await _context.SaveChangesAsync();

        var first = await _service.ListRoles(CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Single(first.Value!);

        // Mutate the DB directly, bypassing the service — only a cache hit explains an unchanged
        // second read.
        using (var mutateCtx = _contextFactory.CreateDbContext())
        {
            mutateCtx.Roles.Add(new Forge.Core.Features.Role.Persistence.Role
            {
                Name = "editor",
                Description = "Editor",
                Category = "Default",
                IsActive = true,
                Level = 2
            });
            await mutateCtx.SaveChangesAsync();
        }

        var second = await _service.ListRoles(CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Single(second.Value!);
    }

    [Fact(DisplayName = "CreateRole evicts the RoleList and RoleNames cache tags")]
    public async Task CreateRole_EvictsRoleListAndRoleNamesTags()
    {
        var (listCalls, namesCalls) = await SeedTaggedCacheEntriesAsync();

        var result = await _service.CreateRole(
            "new-role", "New Role", "Default", 1, [], CancellationToken.None);

        Assert.True(result.IsSuccess);

        await AssertBothTagsEvictedAsync(listCalls, namesCalls);
    }

    [Fact(DisplayName = "UpdateRolePermissions evicts the RoleList and RoleNames cache tags")]
    public async Task UpdateRolePermissions_EvictsRoleListAndRoleNamesTags()
    {
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:manage"), "Manage");
        _context.Permissions.Add(perm);
        _context.Roles.Add(new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "manager",
            Description = "Manager",
            Category = "Default",
            IsActive = true,
            Level = 1
        });
        await _context.SaveChangesAsync();

        var (listCalls, namesCalls) = await SeedTaggedCacheEntriesAsync();

        var result = await _service.UpdateRolePermissions(
            "manager", [PermissionKey.Parse("perm:manage")], CancellationToken.None);

        Assert.True(result.IsSuccess);

        await AssertBothTagsEvictedAsync(listCalls, namesCalls);
    }

    [Fact(DisplayName = "UpdateRoleState evicts the RoleList and RoleNames cache tags")]
    public async Task UpdateRoleState_EvictsRoleListAndRoleNamesTags()
    {
        _context.Roles.Add(new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "active-role",
            Description = "Active",
            Category = "Default",
            IsActive = true,
            Level = 1
        });
        await _context.SaveChangesAsync();

        var (listCalls, namesCalls) = await SeedTaggedCacheEntriesAsync();

        var result = await _service.UpdateRoleState("active-role", false, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await AssertBothTagsEvictedAsync(listCalls, namesCalls);
    }

    [Fact(DisplayName = "InvalidateRoleListsAsync evicts the RoleList and RoleNames cache tags")]
    public async Task InvalidateRoleListsAsync_EvictsRoleListAndRoleNamesTags()
    {
        var (listCalls, namesCalls) = await SeedTaggedCacheEntriesAsync();

        await _service.InvalidateRoleListsAsync(CancellationToken.None);

        await AssertBothTagsEvictedAsync(listCalls, namesCalls);
    }

    private async Task<(int[] listCalls, int[] namesCalls)> SeedTaggedCacheEntriesAsync()
    {
        var listCalls = new[] { 0 };
        var namesCalls = new[] { 0 };

        await _cache.GetOrCreateAsync(
            "test:role-list-probe",
            _ => { listCalls[0]++; return ValueTask.FromResult(0); },
            tags: [CacheTags.RoleList]);

        await _cache.GetOrCreateAsync(
            "test:role-names-probe",
            _ => { namesCalls[0]++; return ValueTask.FromResult(0); },
            tags: [CacheTags.RoleNames]);

        return (listCalls, namesCalls);
    }

    private async Task AssertBothTagsEvictedAsync(int[] listCalls, int[] namesCalls)
    {
        await _cache.GetOrCreateAsync(
            "test:role-list-probe",
            _ => { listCalls[0]++; return ValueTask.FromResult(0); },
            tags: [CacheTags.RoleList]);

        await _cache.GetOrCreateAsync(
            "test:role-names-probe",
            _ => { namesCalls[0]++; return ValueTask.FromResult(0); },
            tags: [CacheTags.RoleNames]);

        Assert.Equal(2, listCalls[0]);
        Assert.Equal(2, namesCalls[0]);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<ForgeDbContext>
    {
        private readonly DbContextOptions<ForgeDbContext> _options;

        public TestDbContextFactory(DbContextOptions<ForgeDbContext> options)
        {
            _options = options;
        }

        public ForgeDbContext CreateDbContext() => new(_options);

        public Task<ForgeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ForgeDbContext(_options));
    }
}
