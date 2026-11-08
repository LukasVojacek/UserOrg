using Forge.Core.Features.Permission.Services;
using Forge.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Forge.Core.Tests.Features.Permission;

/// <summary>
/// Tests for <see cref="PermissionService.DeletePermission"/>, the live permission delete path behind the
/// Gateway role-management console: a permission held by a role is refused and kept, an unheld one is
/// removed, and each outcome logs its own event.
/// </summary>
public sealed class PermissionServiceTests : IDisposable
{
    private const int DeletedPermissionEventId = 1107;
    private const int PermissionDeleteRefusedAssignedEventId = 1108;
    private const int CreatedPermissionEventId = 1103;
    private const int FailedImportPermissionItemEventId = 1104;

    private static readonly PermissionKey TestKey = PermissionKey.Parse("perm:test-delete");

    private readonly ForgeDbContext _context;
    private readonly TestDbContextFactory _contextFactory;
    private readonly CapturingLogger<PermissionService> _logger = new();
    private readonly PermissionService _service;

    public PermissionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ForgeDbContext(options);
        _contextFactory = new TestDbContextFactory(options);
        _service = new PermissionService(_contextFactory, _logger);
    }

    [Fact(DisplayName = "DeletePermission refuses with PermissionAssignedToRoles and keeps the permission when a role holds it")]
    public async Task DeletePermission_RefusesWithPermissionAssignedToRoles_AndKeepsThePermission_WhenARoleHoldsIt()
    {
        await SeedPermissionAsync(heldByRole: true);

        var result = await _service.DeletePermission(TestKey, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("PermissionAssignedToRoles", result.Error.Code);
        await using var fresh = await _contextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.True(await fresh.Permissions.AnyAsync(p => p.Key == TestKey, TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "DeletePermission removes the permission when no role holds it")]
    public async Task DeletePermission_RemovesThePermission_WhenNoRoleHoldsIt()
    {
        await SeedPermissionAsync(heldByRole: false);

        var result = await _service.DeletePermission(TestKey, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await using var fresh = await _contextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.False(await fresh.Permissions.AnyAsync(p => p.Key == TestKey, TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "DeletePermission fails with PermissionNotFound when the key is unknown")]
    public async Task DeletePermission_FailsWithPermissionNotFound_WhenTheKeyIsUnknown()
    {
        var result = await _service.DeletePermission(PermissionKey.Parse("perm:missing"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("PermissionNotFound", result.Error.Code);
    }

    [Fact(DisplayName = "DeletePermission logs PermissionDeleteRefusedAssigned when a role holds it")]
    public async Task DeletePermission_LogsPermissionDeleteRefusedAssigned_WhenARoleHoldsIt()
    {
        await SeedPermissionAsync(heldByRole: true);

        await _service.DeletePermission(TestKey, TestContext.Current.CancellationToken);

        Assert.Contains(PermissionDeleteRefusedAssignedEventId, _logger.EventIds);
        Assert.DoesNotContain(FailedImportPermissionItemEventId, _logger.EventIds);
    }

    [Fact(DisplayName = "DeletePermission logs DeletedPermission when removed")]
    public async Task DeletePermission_LogsDeletedPermission_WhenRemoved()
    {
        await SeedPermissionAsync(heldByRole: false);

        await _service.DeletePermission(TestKey, TestContext.Current.CancellationToken);

        Assert.Contains(DeletedPermissionEventId, _logger.EventIds);
        Assert.DoesNotContain(CreatedPermissionEventId, _logger.EventIds);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    private async Task SeedPermissionAsync(bool heldByRole)
    {
        var permission = new Forge.Core.Features.Permission.Persistence.Permission(TestKey, "d");
        var other = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:test-other"), "o");
        _context.Permissions.Add(permission);
        _context.Roles.Add(new Forge.Core.Features.Role.Persistence.Role
        {
            Name = "holder",
            Category = "Default",
            RolePermissions = heldByRole ? [other, permission] : [other]
        });

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
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

    /// <summary>Minimal <see cref="ILogger{T}"/> recording the event id of every log call, so a test can
    /// assert which log event an outcome emitted.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<int> EventIds { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            EventIds.Add(eventId.Id);
        }
    }
}
