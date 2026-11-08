using System.Reflection;
using System.Text;
using System.Text.Json;

using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission;
using Forge.Core.Features.Role;
using Forge.Core.Features.Role.Services;
using Forge.Core.Persistence;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Shared;



namespace Forge.Core.Tests.Features.Permission;

public sealed class PermissionImportExportTests : IDisposable
{
    private readonly ForgeDbContext _context;
    private readonly IDbContextFactory<ForgeDbContext> _contextFactory;
    private readonly RecordingRoleService _roleService;
    private readonly PermissionImportExport _importExport;

    // Test output is written to Console to avoid dependency on xUnit Abstractions package
    public PermissionImportExportTests()
    {
        // no-op constructor
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ForgeDbContext(options);
        _contextFactory = new TestDbContextFactory(options);
        _roleService = new RecordingRoleService();
        _importExport = new PermissionImportExport(_contextFactory, _roleService, NullLogger<PermissionImportExport>.Instance);
    }

    [Fact(DisplayName = "ExportAsync writes permissions as JSON array")]
    public async Task ExportAsync_WritesPermissionsAsJsonArray()
    {
        // Arrange
        var perm1 = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:test-permission1"), "Description 1");
        var perm2 = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:test-permission2"), "Description 2");

        _context.Permissions.AddRange(perm1, perm2);
        await _context.SaveChangesAsync();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        var options = new ExportOptions();

        // Act
        writer.WriteStartObject();
        writer.WritePropertyName("permissions");
        await _importExport.ExportAsync(writer, options, CancellationToken.None);
        writer.WriteEndObject();
        await writer.FlushAsync();

        // Assert
        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("permissions", out var permsObj));
        Assert.True(permsObj.TryGetProperty("count", out var count));
        Assert.Equal(2, count.GetInt32());
        Assert.True(permsObj.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(2, items.GetArrayLength());
    }

    [Fact(DisplayName = "ImportAsync creates new permissions")]
    public async Task ImportAsync_CreatesNewPermissions()
    {
        // Arrange
        var json = """
        {
            "items": [
                {
                    "key": "perm:test-permission1",
                    "description": "Test Permission 1"
                },
                {
                    "key": "perm:test-permission2",
                    "description": "Test Permission 2"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Console.WriteLine($"Import result errors: {string.Join(';', result.Errors ?? Enumerable.Empty<string>())}");
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? Enumerable.Empty<string>())}");
        using var verifyCtx1 = _contextFactory.CreateDbContext();
        Assert.Equal(2, await verifyCtx1.Permissions.CountAsync());

        var perm1 = await verifyCtx1.Permissions.FirstOrDefaultAsync(p => p.Key == PermissionKey.Parse("perm:test-permission1"));
        Assert.NotNull(perm1);
        Assert.Equal("Test Permission 1", perm1.Description);
    }

    [Fact(DisplayName = "ImportAsync updates existing permissions when OverwriteExisting is true")]
    public async Task ImportAsync_UpdatesExistingPermissions()
    {
        // Arrange
        var existingPerm = new Forge.Core.Features.Permission.Persistence.Permission(
            PermissionKey.Parse("perm:test-permission1"),
            "Old Description");

        _context.Permissions.Add(existingPerm);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "key": "perm:test-permission1",
                    "description": "New Description"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: true);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Console.WriteLine($"Import result errors: {string.Join(';', result.Errors ?? Enumerable.Empty<string>())}");
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? Enumerable.Empty<string>())}");
        using var verifyCtx2 = _contextFactory.CreateDbContext();
        Assert.Equal(1, await verifyCtx2.Permissions.CountAsync());

        var updated = await verifyCtx2.Permissions.FirstOrDefaultAsync(p => p.Key == PermissionKey.Parse("perm:test-permission1"));
        Assert.NotNull(updated);
        Assert.Equal("New Description", updated.Description);
    }

    [Fact(DisplayName = "ImportAsync skips existing permissions when OverwriteExisting is false")]
    public async Task ImportAsync_SkipsExistingPermissions()
    {
        // Arrange
        var existingPerm = new Forge.Core.Features.Permission.Persistence.Permission(
            PermissionKey.Parse("perm:test-permission1"),
            "Old Description");

        _context.Permissions.Add(existingPerm);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "key": "perm:test-permission1",
                    "description": "New Description"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Console.WriteLine($"Import result errors: {string.Join(';', result.Errors ?? Enumerable.Empty<string>())}");
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? Enumerable.Empty<string>())}");
        using var verifyCtx3 = _contextFactory.CreateDbContext();

        var unchanged = await verifyCtx3.Permissions.FirstOrDefaultAsync(p => p.Key == PermissionKey.Parse("perm:test-permission1"));
        Assert.NotNull(unchanged);
        Assert.Equal("Old Description", unchanged.Description);
    }

    [Fact(DisplayName = "ImportAsync returns errors for invalid permission keys")]
    public async Task ImportAsync_ReturnsErrorsForInvalidKeys()
    {
        // Arrange
        var json = """
        {
            "items": [
                {
                    "key": "invalid-key-format",
                    "description": "This should fail"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Assert.False(result.Success, "Expected import to fail for invalid keys");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Invalid permission key", result.Errors.First());
    }

    [Fact(DisplayName = "ImportAsync returns error for missing items array")]
    public async Task ImportAsync_ReturnsErrorForMissingItems()
    {
        // Arrange
        var json = """
        {
            "notItems": []
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Missing or invalid 'items' array", result.Errors.First());
    }

    [Fact(DisplayName = "Overwriting an existing permission's description invalidates the role-list cache")]
    public async Task ImportAsync_OverwriteExisting_InvalidatesRoleListCache()
    {
        // Arrange: role lists cache RoleInfo, which embeds each permission's Description
        // (Role.RolePermissions -> ToPermissionInfo), so a description overwrite must evict it.
        var existingPerm = new Forge.Core.Features.Permission.Persistence.Permission(
            PermissionKey.Parse("perm:test-permission1"),
            "Old Description");

        _context.Permissions.Add(existingPerm);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "key": "perm:test-permission1",
                    "description": "New Description"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: true);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Assert.True(result.Success, string.Join(';', result.Errors ?? Enumerable.Empty<string>()));
        Assert.Equal(1, _roleService.InvalidateCallCount);
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

        public ForgeDbContext CreateDbContext()
        {
            return new ForgeDbContext(_options);
        }

        public Task<ForgeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ForgeDbContext(_options));
        }
    }

    private sealed class RecordingRoleService : IRoleService
    {
        public int InvalidateCallCount { get; private set; }

        public Task InvalidateRoleListsAsync(CancellationToken cancellationToken)
        {
            InvalidateCallCount++;

            return Task.CompletedTask;
        }

        public Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}