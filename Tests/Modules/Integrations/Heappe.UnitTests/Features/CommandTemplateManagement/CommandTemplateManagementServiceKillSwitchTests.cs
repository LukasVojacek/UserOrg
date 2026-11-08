using Heappe.Contracts.Entries;
using Heappe.Features.CommandTemplateManagement;
using Heappe.Features.CommandTemplateManagement.GetAll;
using Heappe.Features.CommandTemplateManagement.GetAll.Response;
using Heappe.Features.KillSwitch;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using Moq;

using UserOrg.Shared;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CommandTemplateManagement;

/// <summary>
/// Pins the kill-aware UI rollup (ADR-0077 §3 bullet 3):
/// <see cref="CommandTemplateManagementService.GetCommandTemplatesForProjectAsync"/> adds a
/// <c>Frozen</c> flag when any of a template's partitions sits on a frozen (killed,
/// non-whitelisted) location, and degrades the flattened <c>State</c> accordingly. `reason` and
/// enforcement metadata never cross this boundary — only the boolean.
/// </summary>
public sealed class CommandTemplateManagementServiceKillSwitchTests
{
    private static readonly Guid _projectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _resourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private const string ProjectShortName = "TestProject";
    private const string ResourceName = "TestResource";

    [Fact(DisplayName = "A partition on a killed, non-whitelisted location marks the template Frozen and degrades its effective State")]
    public async Task Should_set_Frozen_And_degrade_state_When_partition_on_killed_location()
    {
        var (service, templateId) = await CreateServiceAsync(killLocation: true, whitelistTemplate: false);

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.True(template.Frozen);
        Assert.Equal(ECommandTemplateStateResponse.Inactive, template.State);
    }

    [Fact(DisplayName = "A template with no killed partitions is not Frozen and its State is unchanged")]
    public async Task Should_leave_Frozen_false_And_state_unchanged_When_no_killed_partitions()
    {
        var (service, templateId) = await CreateServiceAsync(killLocation: false, whitelistTemplate: false);

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.False(template.Frozen);
        Assert.Equal(ECommandTemplateStateResponse.Active, template.State);
    }

    [Fact(DisplayName = "A whitelisted template on a killed location is not treated as Frozen")]
    public async Task Should_leave_Frozen_false_When_partition_whitelisted_on_killed_location()
    {
        var (service, templateId) = await CreateServiceAsync(killLocation: true, whitelistTemplate: true);

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.False(template.Frozen);
        Assert.Equal(ECommandTemplateStateResponse.Active, template.State);
    }

    [Fact(DisplayName = "Response DTO carries no admin-only kill-switch fields (reason is admin-only, ADR-0077 consequences)")]
    public void Response_DTO_does_not_expose_reason_or_enforcement_metadata()
    {
        var propertyNames = typeof(CommandTemplateResponse).GetProperties().Select(p => p.Name);

        Assert.DoesNotContain("Reason", propertyNames);
        Assert.DoesNotContain("EnforcedBy", propertyNames);
        Assert.DoesNotContain("EnforcedAt", propertyNames);
    }

    private static async Task<(CommandTemplateManagementService Service, Guid TemplateId)> CreateServiceAsync(
        bool killLocation, bool whitelistTemplate)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"CmdMgmtKillSwitch_{Guid.NewGuid()}")
            .Options;
        var dbContext = new HeappeIntegrationDbContext(options);

        var locationId = Guid.NewGuid();
        var locationPartition = new LocationPartition
        {
            Id = Guid.NewGuid(),
            Name = "partition-1",
            LocationId = locationId
        };
        dbContext.Set<LocationPartition>().Add(locationPartition);

        var template = ProjectCommandTemplate.Create(
            "TestTemplate", ECommandTemplateType.UserDefined, _projectId, _resourceId);
        template.ProjectCommandTemplatePartitions.Add(new ProjectCommandTemplatePartition
        {
            Id = Guid.NewGuid(),
            State = ECommandTemplateState.Active,
            LocationPartitionId = locationPartition.Id
        });

        dbContext.Set<ProjectCommandTemplate>().Add(template);
        await dbContext.SaveChangesAsync();

        if (killLocation)
        {
            dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
            {
                LocationId = locationId,
                IsEnforced = true,
                Reason = "Suspected RCE, freezing launches",
                EnforcedAtUtc = DateTime.UtcNow,
                EnforcedByUserId = Guid.NewGuid()
            });
        }

        if (whitelistTemplate)
        {
            dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
            {
                LocationId = locationId,
                ProjectCommandTemplateId = template.Id
            });
        }

        await dbContext.SaveChangesAsync();

        var userService = new Mock<IUserorgService>(MockBehavior.Strict);
        userService
            .Setup(s => s.GetProjectResourceAllocationAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoEntry, Error>.Success(
                new ProjectResourceInfoEntry(
                    _projectId, "Project", ProjectShortName, null,
                    DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1),
                    new ResourceInfoEntry(_resourceId, ResourceName, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1)))));

        var cache = new FakeHybridCache();
        var killSwitchStateService = new KillSwitchStateService(dbContext);
        var writeGuard = new CommandTemplateWriteGuard(killSwitchStateService, dbContext);

        return (new CommandTemplateManagementService(dbContext, userService.Object, cache, killSwitchStateService, writeGuard), template.Id);
    }
}
