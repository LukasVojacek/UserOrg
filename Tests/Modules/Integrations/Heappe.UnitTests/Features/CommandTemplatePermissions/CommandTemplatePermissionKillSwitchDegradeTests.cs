using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Features.CommandTemplatePermissions;
using Heappe.Features.CommandTemplatePermissions.Response;
using Heappe.Features.KillSwitch;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.CommandTemplatePermissions;

/// <summary>
/// Pins the enforcement path (ADR-0077 §3 bullet 1): the kill-switch degrades the per-row <c>Enabled</c> flag
/// HEAppE gates launches on, while the row itself is always retained — dropping it would change the contract
/// HEAppE reads. The degrade is exercised on materialized partitions (the shape the query returns) against the
/// real kill-switch state service, because the partition projection itself needs SQL Server.
/// </summary>
public sealed class CommandTemplatePermissionKillSwitchDegradeTests
{
    private const string TemplateName = "template-1";
    private const string QueueName = "queue-1";
    private const string LocationName = "cluster-1";
    private const string ResourceName = "resource-1";
    private const string ProjectShortName = "proj-1";

    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _resourceId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly Guid _templateId = Guid.NewGuid();

    [Fact(DisplayName = "Killed, non-whitelisted partition keeps its row with Enabled=false")]
    public async Task Killed_partition_row_is_retained_with_enabled_false()
    {
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, _locationId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var templates = await ComposeAsync(dbContext, Partition(ECommandTemplateState.Active));

        var template = Assert.Single(templates);
        Assert.Equal(TemplateName, template.Name);
        Assert.False(template.Enabled);
    }

    [Fact(DisplayName = "Whitelisted partition on a killed location keeps Enabled=true")]
    public async Task Whitelisted_partition_on_killed_location_stays_enabled()
    {
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, _locationId);
        AddWhitelist(dbContext, _locationId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var templates = await ComposeAsync(dbContext, Partition(ECommandTemplateState.Active));

        var template = Assert.Single(templates);
        Assert.True(template.Enabled);
    }

    [Fact(DisplayName = "Lockdown degrades a whitelisted partition and still keeps its row")]
    public async Task Lockdown_degrades_even_a_whitelisted_partition()
    {
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, _locationId, ignoreWhitelist: true);
        AddWhitelist(dbContext, _locationId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var templates = await ComposeAsync(dbContext, Partition(ECommandTemplateState.Active));

        var template = Assert.Single(templates);
        Assert.Equal(TemplateName, template.Name);
        Assert.False(template.Enabled);
    }

    [Fact(DisplayName = "Lifting the lockdown restores the whitelisted partition to Enabled=true")]
    public async Task Lifting_restores_the_whitelisted_partition()
    {
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, _locationId, ignoreWhitelist: true, isEnforced: false);
        AddWhitelist(dbContext, _locationId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var templates = await ComposeAsync(dbContext, Partition(ECommandTemplateState.Active));

        var template = Assert.Single(templates);
        Assert.True(template.Enabled);
    }

    [Fact(DisplayName = "Active partition on a location without a kill-switch stays Enabled=true")]
    public async Task Active_partition_on_healthy_location_stays_enabled()
    {
        using var dbContext = CreateDbContext();

        var templates = await ComposeAsync(dbContext, Partition(ECommandTemplateState.Active));

        var template = Assert.Single(templates);
        Assert.True(template.Enabled);
    }

    [Fact(DisplayName = "Inactive partition on a killed location keeps its row with Enabled=false")]
    public async Task Inactive_partition_on_killed_location_is_retained_disabled()
    {
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, _locationId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var templates = await ComposeAsync(dbContext, Partition(ECommandTemplateState.Inactive));

        var template = Assert.Single(templates);
        Assert.Equal(TemplateName, template.Name);
        Assert.False(template.Enabled);
    }

    [Fact(DisplayName = "A kill on one location leaves the same template enabled on another location")]
    public async Task Kill_on_one_location_does_not_degrade_another_location()
    {
        var healthyLocationId = Guid.NewGuid();
        using var dbContext = CreateDbContext();
        AddKillSwitch(dbContext, _locationId);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var permissions = await ComposePermissionsAsync(
            dbContext,
            [Partition(ECommandTemplateState.Active), Partition(ECommandTemplateState.Active, healthyLocationId)],
            [Allocation(_locationId), Allocation(healthyLocationId)]);

        Assert.Equal(2, permissions.Count);
        Assert.False(SingleTemplateOn(permissions, _locationId).Enabled);
        Assert.True(SingleTemplateOn(permissions, healthyLocationId).Enabled);
    }

    [Fact(DisplayName = "Cached permission entries carry the global kill-switch tag")]
    public void Cached_entries_carry_the_kill_switch_global_tag()
    {
        var tags = CommandTemplatePermissionQueryHandler.BuildPermissionTags([Allocation(_locationId)]);

        Assert.Contains(HeappeCacheKeys.KillSwitchGlobalTag, tags);
        Assert.Contains(HeappeCacheKeys.PermissionProjectTag(_projectId), tags);
    }

    private async Task<IEnumerable<CommandTemplateModel>> ComposeAsync(HeappeIntegrationDbContext dbContext, CommandTemplatePartitionRow partition)
    {
        var permissions = await ComposePermissionsAsync(dbContext, [partition], [Allocation(_locationId)]);

        var permission = Assert.Single(permissions);
        Assert.Equal(QueueName, permission.QueueName);
        Assert.Equal(ProjectShortName, permission.ProjectShortName);
        return permission.CommandTemplates;
    }

    private static async Task<List<PermissionModel>> ComposePermissionsAsync(
        HeappeIntegrationDbContext dbContext,
        IReadOnlyCollection<CommandTemplatePartitionRow> partitions,
        IList<ProjectAllocationInfoEntry> allocations)
    {
        var killed = await new KillSwitchStateService(dbContext).GetKilledPartitionsAsync(
            [.. partitions.Select(p => new PartitionKillKey(p.LocationId, p.ProjectCommandTemplateId))],
            TestContext.Current.CancellationToken);

        return CommandTemplatePermissionComposer.Compose(partitions, allocations, killed);
    }

    private static CommandTemplateModel SingleTemplateOn(List<PermissionModel> permissions, Guid locationId)
        => Assert.Single(Assert.Single(permissions, p => p.ClusterName == LocationNameFor(locationId)).CommandTemplates);

    private static string LocationNameFor(Guid locationId) => $"{LocationName}-{locationId}";

    private CommandTemplatePartitionRow Partition(ECommandTemplateState state, Guid? locationId = null)
        => new(
            _projectId,
            _templateId,
            TemplateName,
            state,
            locationId ?? _locationId,
            _resourceId,
            AggregationName: null,
            QueueName);

    private ProjectAllocationInfoEntry Allocation(Guid locationId)
        => new(
            _projectId,
            ProjectShortName,
            [
                new ResourceAllocationInfoEntry(
                    _resourceId,
                    ResourceName,
                    [
                        new LocationAllocationInfo(
                            locationId,
                            LocationNameFor(locationId),
                            AggregationName: null,
                            new HeappeInstanceEntry(new Uri("https://heappe.example"), new Version(4, 3), new HeappeIdentifier("heappe-1")))
                    ])
            ]);

    private static void AddKillSwitch(
        HeappeIntegrationDbContext dbContext,
        Guid locationId,
        bool ignoreWhitelist = false,
        bool isEnforced = true)
        => dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = isEnforced,
            IgnoreWhitelist = ignoreWhitelist,
            Reason = "Suspected RCE, freezing launches",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        });

    private void AddWhitelist(HeappeIntegrationDbContext dbContext, Guid locationId)
        => dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
        {
            LocationId = locationId,
            ProjectCommandTemplateId = _templateId
        });

    private static HeappeIntegrationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"PermissionKillSwitch_{Guid.NewGuid()}")
            .Options;

        return new HeappeIntegrationDbContext(options);
    }
}
