using Heappe.Contracts.Entries;
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

using Microsoft.EntityFrameworkCore;

using Moq;

using UserOrg.Shared;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CommandTemplateManagement;

/// <summary>
/// Verifies that <see cref="ActivateCommandTemplatesEndpoint"/> and
/// <see cref="DeactivateCommandTemplatesEndpoint"/> evict the
/// <c>TemplatesInfo_{projectShortName}</c> cache tag when the operation succeeds.
/// Both handlers delegate to <see cref="CommandTemplateManagementService.UpdateCommandTemplateStateAsync"/>
/// which owns the invalidation logic.
/// </summary>
public sealed class CommandTemplateManagementCacheTests
{
    private static readonly Guid _projectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _resourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private const string ProjectShortName = "TestProject";
    private const string ResourceName = "TestResource";

    [Fact(DisplayName = "Should evict TemplatesInfo cache when ActivateCommandTemplate succeeds")]
    public async Task Should_evict_TemplatesInfo_cache_When_ActivateCommandTemplate_succeeds()
    {
        // Arrange — template currently Inactive → activate is valid
        var (handler, cache, templateId) = CreateActivateHandler(ECommandTemplateState.Inactive);

        var callCount = 0;
        var tag = CacheTags.TemplatesInfo(ProjectShortName);
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("cached-data"); },
            tags: [tag]);
        Assert.Equal(1, callCount); // factory called once to prime the cache

        // Act
        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName),
            CancellationToken.None);

        // Assert — operation succeeded and cache was evicted
        Assert.True(result.IsSuccess);
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("refreshed-data"); },
            tags: [tag]);
        Assert.Equal(2, callCount); // factory called again because tag was removed
    }

    [Fact(DisplayName = "Should evict TemplatesInfo cache when DeactivateCommandTemplate succeeds")]
    public async Task Should_evict_TemplatesInfo_cache_When_DeactivateCommandTemplate_succeeds()
    {
        // Arrange — template currently Active → deactivate is valid
        var (handler, cache, templateId) = CreateDeactivateHandler(ECommandTemplateState.Active);

        var callCount = 0;
        var tag = CacheTags.TemplatesInfo(ProjectShortName);
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("cached-data"); },
            tags: [tag]);
        Assert.Equal(1, callCount);

        // Act
        var result = await handler.Handle(
            new CommandTemplateActivateRequest(templateId, ProjectShortName, ResourceName),
            CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        await cache.GetOrCreateAsync(tag,
            _ => { callCount++; return ValueTask.FromResult("refreshed-data"); },
            tags: [tag]);
        Assert.Equal(2, callCount);
    }

    private (ActivateCommandTemplateHandler Handler, FakeHybridCache Cache, Guid TemplateId)
        CreateActivateHandler(ECommandTemplateState state)
    {
        var (svc, cache, id) = CreateService(state);
        return (new ActivateCommandTemplateHandler(svc), cache, id);
    }

    private (DeactivateCommandTemplateHandler Handler, FakeHybridCache Cache, Guid TemplateId)
        CreateDeactivateHandler(ECommandTemplateState state)
    {
        var (svc, cache, id) = CreateService(state);
        return (new DeactivateCommandTemplateHandler(svc), cache, id);
    }

    private (CommandTemplateManagementService Service, FakeHybridCache Cache, Guid TemplateId)
        CreateService(ECommandTemplateState partitionState)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"CmdMgmtCache_{Guid.NewGuid()}")
            .Options;
        // ponytail: no `using` — the returned service keeps using this context after this method returns
        var dbContext = new HeappeIntegrationDbContext(options);

        // Seed: one template with one partition in the requested state
        var template = ProjectCommandTemplate.Create(
            "TestTemplate", ECommandTemplateType.UserDefined, _projectId, _resourceId);

        var partition = new ProjectCommandTemplatePartition
        {
            Id = Guid.NewGuid(),
            State = partitionState,
            LocationPartitionId = Guid.NewGuid() // FK not enforced by InMemory provider
        };
        template.ProjectCommandTemplatePartitions.Add(partition);

        dbContext.Set<ProjectCommandTemplate>().Add(template);
        dbContext.SaveChanges();

        var userService = new Mock<IUserorgService>(MockBehavior.Strict);
        userService
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync(
                ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(
                new ProjectResourceInfoSyncEntry(
                    _projectId, "Project", ProjectShortName, null,
                    DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                    new ResourceInfoSyncEntry(_resourceId, ResourceName, null, null, null, [])),
                State.Ok));

        var cache = new FakeHybridCache();
        var killSwitchStateService = new KillSwitchStateService(dbContext);
        var writeGuard = new CommandTemplateWriteGuard(killSwitchStateService, dbContext);

        return (new CommandTemplateManagementService(dbContext, userService.Object, cache, killSwitchStateService, writeGuard), cache, template.Id);
    }
}
