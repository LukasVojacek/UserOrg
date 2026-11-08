using Heappe;
using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Features.KillSwitch;
using Heappe.Features.VersionResolver;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Moq;

using Xunit;

namespace Heappe.UnitTests;

/// <summary>
/// Pins the ADR-0077 §3 bullet 2 per-location kill-switch degrade on the v1
/// <see cref="HeappeService.GetActiveCommandTemplatesAsync"/> filter: a template survives while
/// at least one of its partitions sits on a still-active, non-killed location; it disappears only
/// when every one of its partitions is inactive or killed (and not whitelisted).
/// </summary>
public sealed class GetActiveCommandTemplatesAsyncKillSwitchTests
{
    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _resourceId = Guid.NewGuid();

    [Fact(DisplayName = "Template with one killed-location partition and one healthy-location partition still appears")]
    public async Task Template_survives_when_one_partition_is_healthy()
    {
        var killedLocationId = Guid.NewGuid();
        var healthyLocationId = Guid.NewGuid();
        const long killedPartitionTemplateId = 10L;
        const long healthyPartitionTemplateId = 11L;

        var service = await CreateServiceAsync(ctx =>
        {
            var template = ProjectCommandTemplate.Create("mixed-locations-template", ECommandTemplateType.Unknown, _projectId, _resourceId);
            ctx.Set<ProjectCommandTemplate>().Add(template);
            AddPartition(ctx, template, killedPartitionTemplateId, ECommandTemplateState.Active, killedLocationId);
            AddPartition(ctx, template, healthyPartitionTemplateId, ECommandTemplateState.Active, healthyLocationId);
            AddKillSwitch(ctx, killedLocationId);
        });

        var result = await service.GetActiveCommandTemplatesAsync(_projectId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r => r.Name == "mixed-locations-template" && r.CommandTemplateId == healthyPartitionTemplateId);
        Assert.DoesNotContain(result.Value, r => r.CommandTemplateId == killedPartitionTemplateId);
    }

    [Fact(DisplayName = "Template disappears when all its partitions are killed or inactive")]
    public async Task Template_disappears_when_all_partitions_are_killed_or_inactive()
    {
        var killedLocationId = Guid.NewGuid();
        var otherLocationId = Guid.NewGuid();
        const long killedPartitionTemplateId = 20L;
        const long inactivePartitionTemplateId = 21L;

        var service = await CreateServiceAsync(ctx =>
        {
            var template = ProjectCommandTemplate.Create("fully-frozen-template", ECommandTemplateType.Unknown, _projectId, _resourceId);
            ctx.Set<ProjectCommandTemplate>().Add(template);
            AddPartition(ctx, template, killedPartitionTemplateId, ECommandTemplateState.Active, killedLocationId);
            AddPartition(ctx, template, inactivePartitionTemplateId, ECommandTemplateState.Inactive, otherLocationId);
            AddKillSwitch(ctx, killedLocationId);
        });

        var result = await service.GetActiveCommandTemplatesAsync(_projectId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value, r => r.Name == "fully-frozen-template");
    }

    [Fact(DisplayName = "Whitelisted partition on a killed location still counts as active")]
    public async Task Whitelisted_partition_on_killed_location_still_counts_as_active()
    {
        var killedLocationId = Guid.NewGuid();
        const long templateId = 30L;
        var template = ProjectCommandTemplate.Create("whitelisted-template", ECommandTemplateType.Unknown, _projectId, _resourceId);

        var service = await CreateServiceAsync(ctx =>
        {
            ctx.Set<ProjectCommandTemplate>().Add(template);
            AddPartition(ctx, template, templateId, ECommandTemplateState.Active, killedLocationId);
            AddKillSwitch(ctx, killedLocationId);
            ctx.Set<LocationCommandTemplateWhitelist>().Add(new LocationCommandTemplateWhitelist
            {
                LocationId = killedLocationId,
                ProjectCommandTemplateId = template.Id
            });
        });

        var result = await service.GetActiveCommandTemplatesAsync(_projectId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r => r.Name == "whitelisted-template" && r.CommandTemplateId == templateId);
    }

    private static void AddPartition(HeappeIntegrationDbContext ctx, ProjectCommandTemplate template, long commandTemplateId, ECommandTemplateState state, Guid locationId)
    {
        var locationPartition = new LocationPartition
        {
            Id = Guid.NewGuid(),
            Name = $"queue-{commandTemplateId}",
            LocationId = locationId
        };
        ctx.Set<LocationPartition>().Add(locationPartition);
        ctx.Set<ProjectCommandTemplatePartition>().Add(new ProjectCommandTemplatePartition
        {
            Id = Guid.NewGuid(),
            State = state,
            CommandTemplateId = commandTemplateId,
            LocationPartitionId = locationPartition.Id,
            LocationPartition = locationPartition,
            ProjectCommandTemplate = template
        });
    }

    private static void AddKillSwitch(HeappeIntegrationDbContext ctx, Guid locationId)
        => ctx.Set<LocationCommandTemplateKillSwitch>().Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = locationId,
            IsEnforced = true,
            Reason = "Suspected RCE, freezing launches",
            EnforcedAtUtc = DateTime.UtcNow,
            EnforcedByUserId = Guid.NewGuid()
        });

    private static async Task<IHeappeService> CreateServiceAsync(Action<HeappeIntegrationDbContext> seed)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"HeappeKillSwitchFilter_{Guid.NewGuid()}")
            .Options;

        await using (var seedContext = new HeappeIntegrationDbContext(options))
        {
            seed(seedContext);
            await seedContext.SaveChangesAsync();
        }

        var mockLogger = new Mock<ILogger<HeappeService>>(MockBehavior.Loose);
        var mockHttpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Loose);
        var mockFeatureManager = new Mock<IFeatureManager>(MockBehavior.Loose);
        var mockVersionResolver = new Mock<IHeappeVersionResolver>(MockBehavior.Loose);
        var mockDbContextFactory = new Mock<IDbContextFactory<HeappeIntegrationDbContext>>(MockBehavior.Strict);
        mockDbContextFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new HeappeIntegrationDbContext(options));

        return new HeappeService(
            mockLogger.Object,
            mockHttpClientFactory.Object,
            mockFeatureManager.Object,
            mockDbContextFactory.Object,
            new KillSwitchStateService(new HeappeIntegrationDbContext(options)),
            mockVersionResolver.Object,
            Options.Create(new ClusterAuthTypeOptions()));
    }
}
