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
/// Characterization tests pinning today's (pre-kill-switch) rollup behaviour of
/// <see cref="CommandTemplateManagementService.GetCommandTemplatesForProjectAsync"/>: the
/// flattened <see cref="ECommandTemplateStateResponse"/> a caller sees for a template, derived
/// from <c>ProjectCommandTemplate.State</c> (all-partitions-agree rollup) combined with the
/// allocation's own expiration.
/// </summary>
public sealed class CommandTemplateManagementServiceGetAllTests
{
    private static readonly Guid _projectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _resourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private const string ProjectShortName = "TestProject";
    private const string ResourceName = "TestResource";

    [Fact(DisplayName = "Template with all-Active partitions rolls up to Active when allocation is not expired")]
    public async Task Should_return_Active_When_all_partitions_active_and_allocation_not_expired()
    {
        var (service, templateId) = CreateService(
            partitionStates: [ECommandTemplateState.Active, ECommandTemplateState.Active],
            allocationStartDate: DateTime.UtcNow.AddDays(-1),
            allocationEndDate: DateTime.UtcNow.AddYears(1));

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.Equal(ECommandTemplateStateResponse.Active, template.State);
    }

    [Fact(DisplayName = "Template with all-Inactive partitions rolls up to Inactive")]
    public async Task Should_return_Inactive_When_all_partitions_inactive()
    {
        var (service, templateId) = CreateService(
            partitionStates: [ECommandTemplateState.Inactive, ECommandTemplateState.Inactive],
            allocationStartDate: DateTime.UtcNow.AddDays(-1),
            allocationEndDate: DateTime.UtcNow.AddYears(1));

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.Equal(ECommandTemplateStateResponse.Inactive, template.State);
    }

    [Fact(DisplayName = "Mixed Active/Inactive partitions roll up to Unknown (today's rollup only recognizes all-agree or any-Unknown/Pending)")]
    public async Task Should_return_Unknown_When_active_and_inactive_partitions_disagree()
    {
        var (service, templateId) = CreateService(
            partitionStates: [ECommandTemplateState.Active, ECommandTemplateState.Inactive],
            allocationStartDate: DateTime.UtcNow.AddDays(-1),
            allocationEndDate: DateTime.UtcNow.AddYears(1));

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.Equal(ECommandTemplateStateResponse.Unknown, template.State);
    }

    [Fact(DisplayName = "Partitions with any Pending state roll up to Pending")]
    public async Task Should_return_Pending_When_any_partition_pending()
    {
        var (service, templateId) = CreateService(
            partitionStates: [ECommandTemplateState.Active, ECommandTemplateState.Pending],
            allocationStartDate: DateTime.UtcNow.AddDays(-1),
            allocationEndDate: DateTime.UtcNow.AddYears(1));

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.Equal(ECommandTemplateStateResponse.Pending, template.State);
    }

    [Fact(DisplayName = "Active template on an expired allocation additionally carries the Expired flag")]
    public async Task Should_include_Expired_flag_When_allocation_expired()
    {
        var (service, templateId) = CreateService(
            partitionStates: [ECommandTemplateState.Active],
            allocationStartDate: DateTime.UtcNow.AddYears(-2),
            allocationEndDate: DateTime.UtcNow.AddDays(-1)); // expired

        var result = await service.GetCommandTemplatesForProjectAsync(
            new GetCommandTemplateQuery(ProjectShortName, ResourceName), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var template = Assert.Single(result.Value, t => t.Id == templateId);
        Assert.True(template.State.HasFlag(ECommandTemplateStateResponse.Expired));
        Assert.True(template.State.HasFlag(ECommandTemplateStateResponse.Active));
    }

    private (CommandTemplateManagementService Service, Guid TemplateId) CreateService(
        IReadOnlyList<ECommandTemplateState> partitionStates,
        DateTime allocationStartDate,
        DateTime allocationEndDate)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"CmdMgmtGetAll_{Guid.NewGuid()}")
            .Options;
        var dbContext = new HeappeIntegrationDbContext(options);

        var template = ProjectCommandTemplate.Create(
            "TestTemplate", ECommandTemplateType.UserDefined, _projectId, _resourceId);

        foreach (var state in partitionStates)
        {
            var locationPartition = new LocationPartition
            {
                Id = Guid.NewGuid(),
                Name = $"partition-{Guid.NewGuid()}",
                LocationId = Guid.NewGuid()
            };
            dbContext.Set<LocationPartition>().Add(locationPartition);

            template.ProjectCommandTemplatePartitions.Add(new ProjectCommandTemplatePartition
            {
                Id = Guid.NewGuid(),
                State = state,
                LocationPartitionId = locationPartition.Id
            });
        }

        dbContext.Set<ProjectCommandTemplate>().Add(template);
        dbContext.SaveChanges();

        var userService = new Mock<IUserorgService>(MockBehavior.Strict);
        userService
            .Setup(s => s.GetProjectResourceAllocationAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoEntry, Error>.Success(
                new ProjectResourceInfoEntry(
                    _projectId, "Project", ProjectShortName, null,
                    allocationStartDate, allocationEndDate,
                    new ResourceInfoEntry(_resourceId, ResourceName, null, allocationStartDate, allocationEndDate))));

        var cache = new FakeHybridCache();
        var killSwitchStateService = new KillSwitchStateService(dbContext);
        var writeGuard = new CommandTemplateWriteGuard(killSwitchStateService, dbContext);

        return (new CommandTemplateManagementService(dbContext, userService.Object, cache, killSwitchStateService, writeGuard), template.Id);
    }
}
