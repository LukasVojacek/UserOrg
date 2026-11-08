using Core.UnitTests;

using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Shared;

namespace Forge.Core.Tests.Features.EndpointMap;

/// <summary>
/// Characterization tests for <see cref="EndpointPermissionService"/> covering snapshot cache behaviour
/// and deletion of mappings with attached permissions.
/// </summary>
/// <remarks>
/// Tests verify that the "EndpointPermissionService:Snapshot" HybridCache entry is served from cache
/// on repeated reads and is evicted by every CRUD mutation (Set/Create/Delete). Additionally covers
/// the deletion of endpoint mappings that have associated permissions, including the join-row cleanup
/// required by the foreign key constraints on the join table, validated both in-memory and against
/// real SQL Server. Does NOT cover the importer path (see EndpointMappingImportExportTests) — that
/// path does not evict today, which is exactly the gap this safety net protects.
/// </remarks>
public sealed class EndpointPermissionServiceTests : IDisposable
{
    private const string SnapshotCacheKey = "EndpointPermissionService:Snapshot";

    private readonly ForgeDbContext _context;
    private readonly IDbContextFactory<ForgeDbContext> _contextFactory;
    private readonly HybridCache _cache;
    private readonly EndpointPermissionService _service;

    public EndpointPermissionServiceTests()
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

        _service = new EndpointPermissionService(_contextFactory, _cache, NullLogger<EndpointPermissionService>.Instance);
    }

    [Fact(DisplayName = "GetEndpointPermissionsByPathAsync serves the snapshot from cache on repeated reads")]
    public async Task GetEndpointPermissionsByPathAsync_ServesFromCache_OnRepeatedReads()
    {
        // Arrange
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:read"), "Read");
        _context.Permissions.Add(perm);
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Test.Get",
            PathTemplate = "/items/{id}",
            HttpMethod = "GET",
            Permissions = [perm]
        });
        await _context.SaveChangesAsync();

        // Act — first read builds the snapshot from the DB
        var first = await _service.GetEndpointPermissionsByPathAsync("/items/42", "GET", CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal("Test.Get", first.Value!.EndpointName);

        // Mutate the DB directly, bypassing the service, so only a cache hit could explain an
        // unchanged result on the second read.
        using (var mutateCtx = _contextFactory.CreateDbContext())
        {
            var mapping = await mutateCtx.EndpointMappings.FirstAsync(e => e.EndpointName == "Test.Get");
            mapping.EndpointName = "Test.Get.Renamed";
            await mutateCtx.SaveChangesAsync();
        }

        // Assert — second read still reflects the stale (pre-mutation) snapshot
        var second = await _service.GetEndpointPermissionsByPathAsync("/items/42", "GET", CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal("Test.Get", second.Value!.EndpointName);
    }

    /// <summary>
    /// The gateway names every proxied trace span after this template, so it has to come back with its
    /// placeholders standing. Substituted, the name would carry the request's own identifiers and there
    /// would be one distinct span name per project, per id - the cardinality explosion the template exists
    /// to avoid.
    /// </summary>
    [Fact(DisplayName = "GetEndpointPermissionsByPathAsync returns the path template unsubstituted")]
    public async Task GetEndpointPermissionsByPathAsync_ReturnsThePathTemplate_Unsubstituted()
    {
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:read"), "Read");
        _context.Permissions.Add(perm);
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Templates Info",
            PathTemplate = "/api/Project/TemplatesInfo/{projectShortName}",
            HttpMethod = "GET",
            Permissions = [perm]
        });
        await _context.SaveChangesAsync();

        var match = await _service.GetEndpointPermissionsByPathAsync(
            "/api/Project/TemplatesInfo/TESTPRJ", "GET", CancellationToken.None);

        Assert.True(match.IsSuccess);
        Assert.Equal("/api/Project/TemplatesInfo/{projectShortName}", match.Value!.PathTemplate);
        Assert.DoesNotContain("TESTPRJ", match.Value!.PathTemplate!, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SetEndpointPermissionAsync evicts the snapshot cache entry")]
    public async Task SetEndpointPermissionAsync_EvictsSnapshotCache()
    {
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:set"), "Set");
        _context.Permissions.Add(perm);
        await _context.SaveChangesAsync();

        var factoryCallCount = 0;
        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        var result = await _service.SetEndpointPermissionAsync(
            new EndpointPermissionMap("Test.Set", ["perm:set"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        Assert.Equal(2, factoryCallCount);
    }

    [Fact(DisplayName = "CreateEndpointMappingAsync evicts the snapshot cache entry")]
    public async Task CreateEndpointMappingAsync_EvictsSnapshotCache()
    {
        var factoryCallCount = 0;
        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        var result = await _service.CreateEndpointMappingAsync(
            "Test.Create", "/create/{id}", "POST", null, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        Assert.Equal(2, factoryCallCount);
    }

    [Fact(DisplayName = "DeleteEndpointMappingAsync evicts the snapshot cache entry")]
    public async Task DeleteEndpointMappingAsync_EvictsSnapshotCache()
    {
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Test.Delete",
            PathTemplate = "/delete/{id}",
            HttpMethod = "DELETE"
        });
        await _context.SaveChangesAsync();

        var factoryCallCount = 0;
        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        var result = await _service.DeleteEndpointMappingAsync("Test.Delete", CancellationToken.None);

        Assert.True(result.IsSuccess);

        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        Assert.Equal(2, factoryCallCount);
    }

    [Fact(DisplayName = "DeleteEndpointMappingAsync removes a mapping that has permissions attached, deleting its join rows while the permissions survive")]
    public async Task DeleteEndpointMappingAsync_WithPermissionsAttached_RemovesMappingAndJoinRows_PreservesPermissions()
    {
        var permission = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:delete-with-perms"), "Delete with perms");
        _context.Permissions.Add(permission);
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Test.DeleteWithPermissions",
            PathTemplate = "/delete-with-perms/{id}",
            HttpMethod = "DELETE",
            Permissions = [permission]
        });
        await _context.SaveChangesAsync();

        var result = await _service.DeleteEndpointMappingAsync("Test.DeleteWithPermissions", CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var verifyContext = _contextFactory.CreateDbContext();
        var mappingStillExists = await verifyContext.EndpointMappings
            .AnyAsync(e => e.EndpointName == "Test.DeleteWithPermissions");
        Assert.False(mappingStillExists);

        var joinRowsStillExist = await verifyContext.Set<Dictionary<string, object>>("EndpointMapping_Permission")
            .AnyAsync(row => (long)row["PermissionId"] == permission.Id);
        Assert.False(joinRowsStillExist);

        var permissionSurvives = await verifyContext.Permissions
            .AnyAsync(p => p.Key == permission.Key);
        Assert.True(permissionSurvives);
    }

    [Trait("Category", "Integration")]
    [Fact(DisplayName = "DeleteEndpointMappingAsync deletes join rows before the parent row against real SQL Server (#252 FK_EndpointMapping_Permission_EndpointMappings_EndpointMappingId)")]
    public async Task DeleteEndpointMappingAsync_WithPermissionsAttached_SucceedsAgainstRealSqlServer()
    {
        IntegrationDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var sqlOptions = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlServer(IntegrationDb.ConnectionString)
            .Options;
        var sqlContextFactory = new TestDbContextFactory(sqlOptions);
        var service = new EndpointPermissionService(sqlContextFactory, _cache, NullLogger<EndpointPermissionService>.Instance);

        var token = Guid.NewGuid().ToString("N");
        var endpointName = $"delete-with-permissions-{token}";
        var permissionKey = PermissionKey.Parse($"perm:delete-with-permissions-{token}");

        long permissionId;
        using (var seedContext = sqlContextFactory.CreateDbContext())
        {
            var permission = new Forge.Core.Features.Permission.Persistence.Permission(permissionKey, "Regression test permission (#252)");
            seedContext.Permissions.Add(permission);
            seedContext.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
            {
                EndpointName = endpointName,
                PathTemplate = $"/delete-with-permissions/{token}",
                HttpMethod = "DELETE",
                Permissions = [permission]
            });
            await seedContext.SaveChangesAsync(ct);
            permissionId = permission.Id;
        }

        try
        {
            var result = await service.DeleteEndpointMappingAsync(endpointName, ct);

            Assert.True(result.IsSuccess);

            using var verifyContext = sqlContextFactory.CreateDbContext();
            var mappingStillExists = await verifyContext.EndpointMappings
                .AnyAsync(e => e.EndpointName == endpointName, ct);
            Assert.False(mappingStillExists);

            var joinRowsStillExist = await verifyContext.Set<Dictionary<string, object>>("EndpointMapping_Permission")
                .AnyAsync(row => (long)row["PermissionId"] == permissionId, ct);
            Assert.False(joinRowsStillExist);

            var permissionSurvives = await verifyContext.Permissions
                .AnyAsync(p => p.Key == permissionKey, ct);
            Assert.True(permissionSurvives);
        }
        finally
        {
            // Cleanup must not use the test's (possibly already-cancelled) CancellationToken: if the
            // test itself was cancelled or timed out, an already-cancelled token here would make the
            // cleanup throw immediately and leak these seeded rows into the shared USERORG_TEST_DB.
            using var cleanupContext = sqlContextFactory.CreateDbContext();

            var leftoverMapping = await cleanupContext.EndpointMappings
                .Include(e => e.Permissions)
                .FirstOrDefaultAsync(e => e.EndpointName == endpointName, CancellationToken.None);
            if (leftoverMapping is not null)
            {
                leftoverMapping.Permissions.Clear();
                cleanupContext.EndpointMappings.Remove(leftoverMapping);
            }

            var leftoverPermission = await cleanupContext.Permissions
                .FirstOrDefaultAsync(p => p.Key == permissionKey, CancellationToken.None);
            if (leftoverPermission is not null)
            {
                cleanupContext.Permissions.Remove(leftoverPermission);
            }

            await cleanupContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    [Fact(DisplayName = "InvalidateSnapshotAsync evicts the snapshot cache entry so the next read rebuilds from the DB")]
    public async Task InvalidateSnapshotAsync_EvictsSnapshotCache()
    {
        var factoryCallCount = 0;
        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        await _service.InvalidateSnapshotAsync(CancellationToken.None);

        await _cache.GetOrCreateAsync(SnapshotCacheKey, _ => { factoryCallCount++; return ValueTask.FromResult(0); });

        Assert.Equal(2, factoryCallCount);
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
