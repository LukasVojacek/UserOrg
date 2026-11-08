using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Heappe.Features.KillSwitch;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Heappe.UnitTests.Features.KillSwitch;

/// <summary>
/// Pins the shared kill-switch-state decision (ADR-0077 §2, ADR-0086 §3): a partition is
/// effectively killed when its location has an enforced kill-switch AND either that kill-switch
/// is under lockdown (<c>IgnoreWhitelist</c>) or the (location, template) pair is not
/// whitelisted. The rule lives in exactly one place; the three read paths (B04–B06) and
/// the write/sync guards (B07/B08) consume it. No names, no Core call — pure in-module GUIDs.
/// </summary>
public sealed class KillSwitchStateServiceTests
{
    [Fact(DisplayName = "Partition on an enforced-kill location that is NOT whitelisted is killed")]
    public async Task Enforced_and_not_whitelisted_is_killed()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, isEnforced: true);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None);

        Assert.Contains(new PartitionKillKey(locationId, templateId), killed);
    }

    [Fact(DisplayName = "Whitelisted (location, template) on an enforced-kill location is NOT killed")]
    public async Task Whitelisted_on_killed_location_is_not_killed()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, isEnforced: true);
        AddWhitelist(dbContext, locationId, templateId);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None);

        Assert.Empty(killed);
    }

    [Fact(DisplayName = "Partition on a non-killed location is never killed, even with whitelist rows present")]
    public async Task Non_killed_location_is_never_killed()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        // A stray whitelist row must not, by itself, imply anything about kill state.
        AddWhitelist(dbContext, locationId, Guid.NewGuid());
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None);

        Assert.Empty(killed);
    }

    [Fact(DisplayName = "A kill-switch row with IsEnforced=false does not kill")]
    public async Task Not_enforced_kill_switch_does_not_kill()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, isEnforced: false);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None);

        Assert.Empty(killed);
    }

    [Fact(DisplayName = "Batch query returns the correct killed subset for a mixed set")]
    public async Task Batch_returns_correct_subset_for_mixed_set()
    {
        var killedLocation = Guid.NewGuid();
        var whitelistedTemplate = Guid.NewGuid();
        var killedTemplate = Guid.NewGuid();
        var liftedLocation = Guid.NewGuid();
        var healthyLocation = Guid.NewGuid();
        var template = Guid.NewGuid();

        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, killedLocation, isEnforced: true);
        AddWhitelist(dbContext, killedLocation, whitelistedTemplate);
        AddKillSwitch(dbContext, liftedLocation, isEnforced: false);
        await dbContext.SaveChangesAsync();

        var keys = new[]
        {
            new PartitionKillKey(killedLocation, killedTemplate),      // killed
            new PartitionKillKey(killedLocation, whitelistedTemplate), // whitelisted → not killed
            new PartitionKillKey(liftedLocation, template),            // not enforced → not killed
            new PartitionKillKey(healthyLocation, template)            // no row → not killed
        };

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync(keys, CancellationToken.None);

        Assert.Equal([new PartitionKillKey(killedLocation, killedTemplate)], killed);
    }

    [Fact(DisplayName = "An empty partition set short-circuits to an empty result")]
    public async Task Empty_input_returns_empty()
    {
        using var dbContext = CreateDbContext();
        var service = new KillSwitchStateService(dbContext);

        var killed = await service.GetKilledPartitionsAsync([], CancellationToken.None);

        Assert.Empty(killed);
    }

    [Fact(DisplayName = "Location-grain query returns only the locations with an enforced kill-switch")]
    public async Task Enforced_location_query_returns_only_enforced_locations()
    {
        var enforced = Guid.NewGuid();
        var lifted = Guid.NewGuid();
        var healthy = Guid.NewGuid();

        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, enforced, isEnforced: true);
        AddKillSwitch(dbContext, lifted, isEnforced: false);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var result = await service.GetEnforcedKilledLocationIdsAsync([enforced, lifted, healthy], CancellationToken.None);

        Assert.Equal([enforced], result);
    }

    [Fact(DisplayName = "Lockdown (IgnoreWhitelist) kills a whitelisted partition without deleting the whitelist row")]
    public async Task Lockdown_ignores_whitelist_but_keeps_the_rows()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, isEnforced: true, ignoreWhitelist: true);
        AddWhitelist(dbContext, locationId, templateId);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None);

        Assert.Contains(new PartitionKillKey(locationId, templateId), killed);

        // "Ignores", never "deletes": evaluating the rule must not touch the standing whitelist.
        Assert.True(await dbContext.LocationCommandTemplateWhitelists
            .AnyAsync(w => w.LocationId == locationId && w.ProjectCommandTemplateId == templateId));
    }

    [Fact(DisplayName = "Lifting lockdown makes the standing whitelist live again with no re-setup")]
    public async Task Lifting_lockdown_revives_the_standing_whitelist()
    {
        var locationId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        var killSwitch = AddKillSwitch(dbContext, locationId, isEnforced: true, ignoreWhitelist: true);
        AddWhitelist(dbContext, locationId, templateId);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        Assert.NotEmpty(await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None));

        // The only change is the flag — the whitelist row is never re-created.
        killSwitch.IgnoreWhitelist = false;
        await dbContext.SaveChangesAsync();

        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, templateId)], CancellationToken.None);

        Assert.Empty(killed);
    }

    [Fact(DisplayName = "An orphaned whitelist row never un-kills a different partition on the same location")]
    public async Task Orphan_whitelist_row_is_inert()
    {
        var locationId = Guid.NewGuid();
        var realTemplateId = Guid.NewGuid();
        var orphanTemplateId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, locationId, isEnforced: true);
        // A whitelist row can outlive its template (ADR-0086): it matches no partition in the input.
        AddWhitelist(dbContext, locationId, orphanTemplateId);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync([new PartitionKillKey(locationId, realTemplateId)], CancellationToken.None);

        Assert.Equal([new PartitionKillKey(locationId, realTemplateId)], killed);
    }

    [Fact(DisplayName = "Lockdown on one location does not suppress another location's whitelist")]
    public async Task Lockdown_is_scoped_to_its_own_location()
    {
        var lockedDownLocation = Guid.NewGuid();
        var whitelistHonouringLocation = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, lockedDownLocation, isEnforced: true, ignoreWhitelist: true);
        AddWhitelist(dbContext, lockedDownLocation, templateId);
        AddKillSwitch(dbContext, whitelistHonouringLocation, isEnforced: true);
        AddWhitelist(dbContext, whitelistHonouringLocation, templateId);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var killed = await service.GetKilledPartitionsAsync(
            [new PartitionKillKey(lockedDownLocation, templateId), new PartitionKillKey(whitelistHonouringLocation, templateId)],
            CancellationToken.None);

        Assert.Equal([new PartitionKillKey(lockedDownLocation, templateId)], killed);
    }

    [Fact(DisplayName = "Location-grain query is unaffected by lockdown — it already ignores the whitelist")]
    public async Task Location_grain_query_is_unaffected_by_lockdown()
    {
        var lockedDown = Guid.NewGuid();
        var enforced = Guid.NewGuid();

        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, lockedDown, isEnforced: true, ignoreWhitelist: true);
        AddKillSwitch(dbContext, enforced, isEnforced: true);
        await dbContext.SaveChangesAsync();

        var service = new KillSwitchStateService(dbContext);
        var result = await service.GetEnforcedKilledLocationIdsAsync([lockedDown, enforced], CancellationToken.None);

        Assert.Equal(new[] { lockedDown, enforced }.Order(), result.Order());
    }

    private static LocationCommandTemplateKillSwitch AddKillSwitch(HeappeIntegrationDbContext dbContext, Guid locationId, bool isEnforced, bool ignoreWhitelist = false)
    {
        var killSwitch = new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = isEnforced,
            IgnoreWhitelist = ignoreWhitelist,
            Reason = isEnforced ? "Suspected RCE, freezing launches" : "lifted after the incident closed",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        };

        dbContext.LocationCommandTemplateKillSwitches.Add(killSwitch);

        return killSwitch;
    }

    private static void AddWhitelist(HeappeIntegrationDbContext dbContext, Guid locationId, Guid templateId)
        => dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = templateId
        });

    private static HeappeIntegrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"KillSwitchState_{Guid.NewGuid()}")
            .Options;

        return new HeappeIntegrationDbContext(options);
    }
}
