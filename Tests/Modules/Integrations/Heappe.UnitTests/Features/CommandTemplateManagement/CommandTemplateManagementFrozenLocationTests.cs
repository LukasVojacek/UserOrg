using Heappe.Contracts.Entries.Sync;
using Heappe.Features.CommandTemplateManagement;
using Heappe.Features.CommandTemplateManagement.Activate;
using Heappe.Features.CommandTemplateManagement.Deactivate;
using Heappe.Features.CommandTemplateManagement.Request;
using Heappe.Features.KillSwitch;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CommandTemplateManagement;

/// <summary>
/// Pins the B07 write-guard decision (ADR-0077 §5): Activate/Deactivate reject a frozen target
/// Location with 409 (never 403), the message excludes <c>reason</c>, the whitelist does not
/// grant writability, and — mandatory test #5, the heart of the whole kill-switch design — the
/// stored <see cref="ProjectCommandTemplatePartition.State"/> is never mutated by a rejected call.
/// </summary>
public sealed class CommandTemplateManagementFrozenLocationTests
{
    private static readonly Guid _projectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _resourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private const string ProjectShortName = "TestProject";
    private const string ResourceName = "TestResource";
    private const string LocationName = "Karolina";
    private const string ReasonText = "Suspected RCE in BulkCopy - do not disclose";

    [Fact(DisplayName = "Activate: 409 (not 403) when the target location is frozen, message excludes reason, stored State untouched")]
    public async Task Activate_Should_Return409_And_NotMutateState_When_LocationIsFrozen()
    {
        var (service, templateId, partitionId, dbName) = CreateFrozenService(ECommandTemplateState.Inactive, whitelisted: false);
        var handler = new ActivateCommandTemplateHandler(service);

        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName), CancellationToken.None);

        AssertFrozenRejection(result);
        AssertStoredStateUnchanged(dbName, partitionId, ECommandTemplateState.Inactive);
    }

    [Fact(DisplayName = "Deactivate: 409 (not 403) when the target location is frozen, message excludes reason, stored State untouched")]
    public async Task Deactivate_Should_Return409_And_NotMutateState_When_LocationIsFrozen()
    {
        var (service, templateId, partitionId, dbName) = CreateFrozenService(ECommandTemplateState.Active, whitelisted: false);
        var handler = new DeactivateCommandTemplateHandler(service);

        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName), CancellationToken.None);

        AssertFrozenRejection(result);
        AssertStoredStateUnchanged(dbName, partitionId, ECommandTemplateState.Active);
    }

    [Fact(DisplayName = "Activate: still 409 when the template is whitelisted -- whitelist governs runnability, not writability")]
    public async Task Activate_Should_StillReturn409_When_TemplateIsWhitelisted()
    {
        var (service, templateId, partitionId, dbName) = CreateFrozenService(ECommandTemplateState.Inactive, whitelisted: true);
        var handler = new ActivateCommandTemplateHandler(service);

        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName), CancellationToken.None);

        AssertFrozenRejection(result);
        AssertStoredStateUnchanged(dbName, partitionId, ECommandTemplateState.Inactive);
    }

    [Fact(DisplayName = "Activate: 409 when the partition's own location is frozen but absent from the allocation's Resource.Locations -- fail CLOSED, not fail-open on the intersection (F1)")]
    public async Task Activate_Should_Return409_When_PartitionLocation_Is_Frozen_But_Missing_From_ResourceLocations()
    {
        // The partition query uses IgnoreQueryFilters while the Core-side allocation applies its
        // own filters, so the two location sets can legitimately diverge. The guard must still
        // reject on the partition's OWN location — narrowing to the intersection with
        // Resource.Locations would silently let a frozen-but-unlisted location through.
        var (service, templateId, partitionId, dbName) = CreateFrozenService(
            ECommandTemplateState.Inactive, whitelisted: false, includeLocationInAllocation: false);
        var handler = new ActivateCommandTemplateHandler(service);

        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(KillSwitchErrorCodes.LocationFrozen, result.Error.Code);
        Assert.DoesNotContain("RCE", result.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("BulkCopy", result.Error.Description, StringComparison.Ordinal);
        AssertStoredStateUnchanged(dbName, partitionId, ECommandTemplateState.Inactive);
    }

    [Fact(DisplayName = "Deactivate: still 409 (not a 500) when the allocation lists the same location twice")]
    public async Task Deactivate_Should_Return409_When_AllocationListsLocationMoreThanOnce()
    {
        // A Resource can list the same LocationId more than once — one entry per aggregation/queue.
        // The guard used to build its id -> name map with ToDictionary, which throws on the
        // duplicate ("An item with the same key has already been added"). That ran before every
        // Activate/Deactivate, so it turned the whole write path into a 500 — frozen or not, and
        // regardless of the kill-switch. Found by exercising the live API, not by these tests:
        // every other case here builds an allocation with exactly one entry per location.
        var (service, templateId, partitionId, dbName) = CreateFrozenService(
            ECommandTemplateState.Active, whitelisted: false, duplicateLocationInAllocation: true);
        var handler = new DeactivateCommandTemplateHandler(service);

        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName), CancellationToken.None);

        AssertFrozenRejection(result);
        AssertStoredStateUnchanged(dbName, partitionId, ECommandTemplateState.Active);
    }

    private static void AssertFrozenRejection(Result<VoidResult, Error> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(KillSwitchErrorCodes.LocationFrozen, result.Error.Code);
        Assert.Contains(LocationName, result.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("RCE", result.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("BulkCopy", result.Error.Description, StringComparison.Ordinal);

        var httpResult = result.ToWriteHttpResult();
        var problem = Assert.IsType<ProblemHttpResult>(httpResult);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    private static void AssertStoredStateUnchanged(string dbName, Guid partitionId, ECommandTemplateState expectedState)
    {
        using var verifyContext = OpenDbContext(dbName);
        var storedState = verifyContext.Set<ProjectCommandTemplatePartition>()
            .AsNoTracking()
            .Single(p => p.Id == partitionId)
            .State;

        Assert.Equal(expectedState, storedState);
    }

    private (CommandTemplateManagementService Service, Guid TemplateId, Guid PartitionId, string DbName)
        CreateFrozenService(
            ECommandTemplateState partitionState,
            bool whitelisted,
            bool includeLocationInAllocation = true,
            bool duplicateLocationInAllocation = false)
    {
        var dbName = $"CmdMgmtFrozen_{Guid.NewGuid()}";
        var dbContext = OpenDbContext(dbName);

        var locationId = Guid.NewGuid();
        var locationPartition = new LocationPartition { Id = Guid.NewGuid(), Name = "queue1", LocationId = locationId };

        var template = ProjectCommandTemplate.Create("TestTemplate", ECommandTemplateType.UserDefined, _projectId, _resourceId);
        var partition = new ProjectCommandTemplatePartition
        {
            Id = Guid.NewGuid(),
            State = partitionState,
            LocationPartition = locationPartition
        };
        template.ProjectCommandTemplatePartitions.Add(partition);

        dbContext.Set<ProjectCommandTemplate>().Add(template);
        dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = true,
            Reason = ReasonText,
            EnforcedAtUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            EnforcedByUserId = Guid.NewGuid()
        });
        if (whitelisted)
        {
            dbContext.LocationCommandTemplateWhitelists.Add(new LocationCommandTemplateWhitelist
            {
                LocationId = locationId,
                ProjectCommandTemplateId = template.Id
            });
        }
        dbContext.SaveChanges();

        LocationAllocationInfoSyncEntry LocationEntry() => new(
            locationId, LocationName, "login",
            ELocationType.HPC, null,
            new HeappeInstanceSyncEntry(new Uri("http://heappe.test"), new Version(1, 0), new HeappeIdentifier("heappe-id")));

        IReadOnlyList<LocationAllocationInfoSyncEntry> allocationLocations = includeLocationInAllocation
            ? (duplicateLocationInAllocation ? [LocationEntry(), LocationEntry()] : [LocationEntry()])
            : [];

        var userService = new Mock<IUserorgService>(MockBehavior.Strict);
        userService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(
                ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(
                new ProjectResourceInfoSyncEntry(
                    _projectId, "Project", ProjectShortName, null,
                    DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                    new ResourceInfoSyncEntry(
                        _resourceId, ResourceName, null, null, null,
                        allocationLocations)),
                State.Ok));

        var cache = new FakeHybridCache();
        var killSwitchStateService = new KillSwitchStateService(dbContext);
        var writeGuard = new CommandTemplateWriteGuard(killSwitchStateService, dbContext);

        return (new CommandTemplateManagementService(dbContext, userService.Object, cache, killSwitchStateService, writeGuard), template.Id, partition.Id, dbName);
    }

    private static HeappeIntegrationDbContext OpenDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new HeappeIntegrationDbContext(options);
    }
}
