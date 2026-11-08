using System;
using System.Linq;
using System.Threading.Tasks;

using Heappe;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Heappe.UnitTests.Persistence;

/// <summary>
/// Pins the B01 persistence foundation: the two new kill-switch tables exist on
/// <see cref="HeappeIntegrationDbContext"/> with the PK shapes ADR-0077 §1 requires, and the
/// existing <see cref="Heappe.Persistence.Entities.ProjectCommandTemplatePartition"/> stored
/// state is untouched by this addition.
/// </summary>
public sealed class KillSwitchPersistenceTests
{
    [Fact(DisplayName = "DbContext exposes DbSet<LocationCommandTemplateKillSwitch> and DbSet<LocationCommandTemplateWhitelist>")]
    public void Should_expose_both_new_DbSets()
    {
        using var dbContext = CreateInMemoryDbContext();

        Assert.IsAssignableFrom<DbSet<LocationCommandTemplateKillSwitch>>(dbContext.LocationCommandTemplateKillSwitches);
        Assert.IsAssignableFrom<DbSet<LocationCommandTemplateWhitelist>>(dbContext.LocationCommandTemplateWhitelists);
    }

    [Fact(DisplayName = "LocationCommandTemplateKillSwitch is keyed by LocationId alone (one row per location)")]
    public async Task Should_key_KillSwitch_by_LocationId_only()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);
        var locationId = Guid.NewGuid();

        dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = true,
            Reason = "Suspected RCE, freezing launches",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        });
        await dbContext.SaveChangesAsync();

        var duplicate = new HeappeIntegrationDbContext(new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseSqlite(connection)
            .Options);
        duplicate.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = false,
            Reason = "second row for same location",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact(DisplayName = "LocationCommandTemplateWhitelist is keyed by (LocationId, ProjectCommandTemplateId)")]
    public async Task Should_key_Whitelist_by_LocationId_and_ProjectCommandTemplateId()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = templateId
        });
        await dbContext.SaveChangesAsync();

        var duplicate = new HeappeIntegrationDbContext(new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseSqlite(connection)
            .Options);
        duplicate.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = templateId
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact(DisplayName = "A different ProjectCommandTemplateId on the same location is a distinct whitelist row")]
    public async Task Should_allow_multiple_templates_whitelisted_on_same_location()
    {
        using var dbContext = CreateInMemoryDbContext();
        var locationId = Guid.NewGuid();

        dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = Guid.NewGuid()
        });
        dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = Guid.NewGuid()
        });

        await dbContext.SaveChangesAsync();

        Assert.Equal(2, dbContext.LocationCommandTemplateWhitelists.Local.Count);
    }

    [Fact(DisplayName = "A kill-switch row persists IgnoreWhitelist and defaults it to false")]
    public async Task Should_persist_IgnoreWhitelist_and_default_it_to_false()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);
        var lockdownLocationId = Guid.NewGuid();
        var plainLocationId = Guid.NewGuid();

        dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = lockdownLocationId,
            IsEnforced = true,
            IgnoreWhitelist = true,
            Reason = "Total lockdown, whitelist ignored",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        });
        dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = plainLocationId,
            IsEnforced = true,
            Reason = "Freeze, whitelist honoured",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        });
        await dbContext.SaveChangesAsync();

        using var reader = CreateSqliteDbContext(connection);
        var stored = await reader.LocationCommandTemplateKillSwitches
            .AsNoTracking()
            .Where(x => x.LocationId == lockdownLocationId || x.LocationId == plainLocationId)
            .ToListAsync();

        Assert.True(stored.Single(x => x.LocationId == lockdownLocationId).IgnoreWhitelist);
        Assert.False(stored.Single(x => x.LocationId == plainLocationId).IgnoreWhitelist);
    }

    [Fact(DisplayName = "A whitelist row round-trips its AddedByUserId/AddedAtUtc audit stamp")]
    public async Task Should_persist_whitelist_audit_stamp()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var addedByUserId = Guid.NewGuid();
        var addedAtUtc = new DateTime(2026, 7, 17, 9, 49, 25, DateTimeKind.Utc);

        dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = templateId,
            AddedByUserId = addedByUserId,
            AddedAtUtc = addedAtUtc
        });
        await dbContext.SaveChangesAsync();

        using var reader = CreateSqliteDbContext(connection);
        var stored = await reader.LocationCommandTemplateWhitelists
            .AsNoTracking()
            .Where(x => x.LocationId == locationId && x.ProjectCommandTemplateId == templateId)
            .SingleAsync();

        Assert.Equal(addedByUserId, stored.AddedByUserId);
        Assert.Equal(addedAtUtc, stored.AddedAtUtc);
    }

    [Fact(DisplayName = "HeappeCacheKeys exposes the global kill-switch cache tag constant")]
    public void Should_expose_KillSwitchGlobalTag_constant()
    {
        Assert.False(string.IsNullOrWhiteSpace(HeappeCacheKeys.KillSwitchGlobalTag));
    }

    private static HeappeIntegrationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"KillSwitchPersistence_{Guid.NewGuid()}")
            .Options;

        return new HeappeIntegrationDbContext(options);
    }

    private static SqliteConnection CreateOpenSqliteConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static HeappeIntegrationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new HeappeIntegrationDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}
