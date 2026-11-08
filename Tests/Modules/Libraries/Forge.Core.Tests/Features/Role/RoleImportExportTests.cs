using System.Text.Json;

using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Role;
using Forge.Core.Features.Role.Services;
using Forge.Core.Persistence;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Shared;

namespace Forge.Core.Tests.Features.Role;

public sealed class RoleImportExportTests : IDisposable
{
    private readonly ForgeDbContext _context;
    private readonly IDbContextFactory<ForgeDbContext> _contextFactory;
    private readonly RecordingRoleService _roleService;
    private readonly RoleImportExport _importExport;

    public RoleImportExportTests()
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ForgeDbContext(options);
        _contextFactory = new TestDbContextFactory(options);
        _roleService = new RecordingRoleService();
        _importExport = new RoleImportExport(_contextFactory, _roleService, NullLogger<RoleImportExport>.Instance);
    }

    [Fact(DisplayName = "ImportAsync creates new role without permissions")]
    public async Task ImportAsync_CreatesNewRole_WhenNoPermissions()
    {
        // Arrange
        var json = """
        {
            "items": [
                {
                    "name": "viewer",
                    "description": "Viewer role",
                    "category": "Default",
                    "isActive": true,
                    "level": 1
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(1, result.CreatedCount);
        using var verifyCtx = _contextFactory.CreateDbContext();
        var role = await verifyCtx.Roles.FirstOrDefaultAsync(r => r.Name == "viewer");
        Assert.NotNull(role);
        Assert.Equal("Viewer role", role.Description);
        Assert.True(role.IsActive);
        Assert.Equal(1, role.Level);
    }

    [Fact(DisplayName = "ImportAsync creates new role and assigns existing permissions")]
    public async Task ImportAsync_CreatesNewRole_AndAssignsPermissions_WhenPermissionsExist()
    {
        // Arrange
        var perm1 = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:read"), "Read");
        var perm2 = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:write"), "Write");
        _context.Permissions.AddRange(perm1, perm2);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "name": "editor",
                    "description": "Editor role",
                    "category": "Default",
                    "isActive": true,
                    "level": 2,
                    "permissionKeys": ["perm:read", "perm:write"]
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(1, result.CreatedCount);
        using var verifyCtx = _contextFactory.CreateDbContext();
        var role = await verifyCtx.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == "editor");
        Assert.NotNull(role);
        Assert.Equal(2, role.RolePermissions.Count);
        Assert.Contains(role.RolePermissions, p => p.Key.Value == "perm:read");
        Assert.Contains(role.RolePermissions, p => p.Key.Value == "perm:write");
    }

    [Fact(DisplayName = "ImportAsync returns error when referenced permission does not exist")]
    public async Task ImportAsync_ReturnsError_WhenReferencedPermissionDoesNotExist()
    {
        // Arrange — no permissions in DB, role references one
        var json = """
        {
            "items": [
                {
                    "name": "orphan-role",
                    "description": "Role with missing perm",
                    "category": "Default",
                    "isActive": true,
                    "level": 1,
                    "permissionKeys": ["perm:nonexistent"]
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        // Assert
        Assert.False(result.Success, "Import should fail when a referenced permission is missing");
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors!, e => e.Contains("perm:nonexistent"));
        using var verifyCtx = _contextFactory.CreateDbContext();
        var role = await verifyCtx.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == "orphan-role");
        // Role is created but has no permissions assigned
        Assert.NotNull(role);
        Assert.Empty(role.RolePermissions);
    }

    [Fact(DisplayName = "ImportAsync skips existing role when OverwriteExisting is false")]
    public async Task ImportAsync_SkipsExistingRole_WhenOverwriteExistingIsFalse()
    {
        // Arrange
        var existingRole = new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "admin",
            Description = "Original",
            Category = "Default",
            IsActive = true,
            Level = 10
        };
        _context.Roles.Add(existingRole);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "name": "admin",
                    "description": "Updated",
                    "category": "Default",
                    "isActive": true,
                    "level": 99
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        using var verifyCtx = _contextFactory.CreateDbContext();
        var role = await verifyCtx.Roles.FirstOrDefaultAsync(r => r.Name == "admin");
        Assert.NotNull(role);
        Assert.Equal("Original", role.Description);
        Assert.Equal(10, role.Level);
    }

    [Fact(DisplayName = "ImportAsync updates existing role when OverwriteExisting is true")]
    public async Task ImportAsync_UpdatesExistingRole_WhenOverwriteExistingIsTrue()
    {
        // Arrange
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:manage"), "Manage");
        var existingRole = new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "manager",
            Description = "Old Description",
            Category = "Legacy",
            IsActive = false,
            Level = 5
        };
        _context.Permissions.Add(perm);
        _context.Roles.Add(existingRole);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "name": "manager",
                    "description": "New Description",
                    "category": "Core",
                    "isActive": true,
                    "level": 50,
                    "permissionKeys": ["perm:manage"]
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: true);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(1, result.UpdatedCount);
        using var verifyCtx = _contextFactory.CreateDbContext();
        var role = await verifyCtx.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == "manager");
        Assert.NotNull(role);
        Assert.Equal("New Description", role.Description);
        Assert.Equal("Core", role.Category);
        Assert.True(role.IsActive);
        Assert.Equal(50, role.Level);
        Assert.Single(role.RolePermissions);
        Assert.Contains(role.RolePermissions, p => p.Key.Value == "perm:manage");
    }

    [Fact(DisplayName = "ImportAsync returns error for missing items array")]
    public async Task ImportAsync_ReturnsError_WhenItemsArrayMissing()
    {
        // Arrange
        var json = """{ "notItems": [] }""";
        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors!, e => e.Contains("items"));
    }

    [Fact(DisplayName = "ImportAsync parses isActive false correctly")]
    public async Task ImportAsync_ParsesIsActiveFalse_Correctly()
    {
        // Arrange
        var json = """
        {
            "items": [
                {
                    "name": "inactive-role",
                    "description": "Inactive",
                    "category": "Default",
                    "isActive": false,
                    "level": 1
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        // Act
        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        using var verifyCtx = _contextFactory.CreateDbContext();
        var role = await verifyCtx.Roles.FirstOrDefaultAsync(r => r.Name == "inactive-role");
        Assert.NotNull(role);
        Assert.False(role.IsActive);
    }

    [Fact(DisplayName = "ExportAsync writes roles with permission keys as JSON")]
    public async Task ExportAsync_WritesRolesWithPermissions_AsJson()
    {
        // Arrange
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:view"), "View");
        _context.Permissions.Add(perm);
        await _context.SaveChangesAsync();

        var role = new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "viewer",
            Description = "Viewer",
            Category = "Default",
            IsActive = true,
            Level = 1,
            RolePermissions = [perm]
        };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        var exportOptions = new ExportOptions();

        // Act
        writer.WriteStartObject();
        writer.WritePropertyName("roles");
        await _importExport.ExportAsync(writer, exportOptions, CancellationToken.None);
        writer.WriteEndObject();
        await writer.FlushAsync();

        // Assert
        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream);
        var rolesObj = json.RootElement.GetProperty("roles");
        Assert.Equal(1, rolesObj.GetProperty("count").GetInt32());
        var items = rolesObj.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var item = items.EnumerateArray().First();
        Assert.Equal("viewer", item.GetProperty("name").GetString());
        var permKeys = item.GetProperty("permissionKeys");
        Assert.Equal(1, permKeys.GetArrayLength());
        Assert.Equal("perm:view", permKeys.EnumerateArray().First().GetString());
    }

    [Fact(DisplayName = "#210: ImportAsync invalidates the role lists after a successful import")]
    public async Task ImportAsync_InvalidatesRoleLists_AfterSuccessfulImport()
    {
        var json = """
        {
            "items": [
                { "name": "viewer", "description": "Viewer role", "category": "Default", "isActive": true, "level": 1 }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, _roleService.InvalidateCallCount);
    }

    [Fact(DisplayName = "#210: ImportAsync invalidates the role lists even when the import reported errors over committed rows")]
    public async Task ImportAsync_InvalidatesRoleLists_WhenImportReportedErrorsOverCommittedRows()
    {
        // A role naming an unknown permission is recorded as an error, yet the role itself is still
        // committed by the single SaveChangesAsync — the partial-write shape ADR-0084 §2 names when
        // it forbids gating invalidation on ImportResult.Success.
        var json = """
        {
            "items": [
                { "name": "orphan-role", "description": "Role with missing perm", "category": "Default", "isActive": true, "level": 1, "permissionKeys": ["perm:nonexistent"] }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, _roleService.InvalidateCallCount);
    }

    [Fact(DisplayName = "#210: ImportAsync invalidates the role lists even when the import wrote nothing")]
    public async Task ImportAsync_InvalidatesRoleLists_WhenImportWroteNothing()
    {
        _context.Roles.Add(new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "admin",
            Description = "Original",
            Category = "Default",
            IsActive = true,
            Level = 10
        });
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                { "name": "admin", "description": "Updated", "category": "Default", "isActive": true, "level": 99 }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var importOptions = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, importOptions, CancellationToken.None);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, _roleService.InvalidateCallCount);
    }

    public void Dispose()
    {
        _context?.Dispose();
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
