using Heappe;
using Heappe.Contracts;
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
/// Characterization tests pinning today's (pre-kill-switch) DB-backed filter of
/// <see cref="HeappeService.GetActiveCommandTemplatesAsync"/>: only
/// <see cref="ECommandTemplateState.Active"/> partitions for the given project are returned;
/// this is the v1 <c>TemplatesInfo</c> filter that will later gain a <c>&amp;&amp; !killed</c> condition.
/// </summary>
public sealed class GetActiveCommandTemplatesAsyncTests
{
    private static readonly Guid _projectId = Guid.NewGuid();
    private static readonly Guid _otherProjectId = Guid.NewGuid();
    private static readonly Guid _resourceId = Guid.NewGuid();

    [Fact(DisplayName = "Returns only Active partitions for the requested project, excluding Inactive and other-project rows")]
    public async Task Should_return_only_Active_partitions_for_the_project()
    {
        var (service, activeTemplateId) = await CreateServiceAsync(seedMixedStates: true);

        var result = await service.GetActiveCommandTemplatesAsync(_projectId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value);
        Assert.Equal("active-template", entry.Name);
        Assert.Equal(activeTemplateId, entry.CommandTemplateId);
    }

    [Fact(DisplayName = "Returns empty set when the project has no Active partitions")]
    public async Task Should_return_empty_When_no_active_partitions_for_project()
    {
        var (service, _) = await CreateServiceAsync(seedMixedStates: false);

        var result = await service.GetActiveCommandTemplatesAsync(_projectId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    /// <summary>
    /// Seeds one Active partition + one Inactive partition on the requested project, plus an
    /// Active partition on a DIFFERENT project — pinning that the project filter is applied and
    /// that the Inactive partition is excluded today (no kill-switch flag exists yet).
    /// When <paramref name="seedMixedStates"/> is false, only the Inactive/other-project rows are
    /// seeded (no Active row for the requested project).
    /// </summary>
    private static async Task<(IHeappeService Service, long ActiveTemplateId)> CreateServiceAsync(bool seedMixedStates)
    {
        var dbName = $"HeappeActiveTemplates_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        const long activeTemplateId = 1L;
        const long inactiveTemplateId = 2L;
        const long otherProjectTemplateId = 3L;

        await using (var seedContext = new HeappeIntegrationDbContext(options))
        {
            var inactiveTemplate = ProjectCommandTemplate.Create("inactive-template", ECommandTemplateType.Unknown, _projectId, _resourceId);
            seedContext.Set<ProjectCommandTemplate>().Add(inactiveTemplate);
            seedContext.Set<ProjectCommandTemplatePartition>().Add(new ProjectCommandTemplatePartition
            {
                Id = Guid.NewGuid(),
                State = ECommandTemplateState.Inactive,
                CommandTemplateId = inactiveTemplateId,
                LocationPartitionId = Guid.NewGuid(),
                ProjectCommandTemplate = inactiveTemplate
            });

            var otherProjectTemplate = ProjectCommandTemplate.Create("other-project-template", ECommandTemplateType.Unknown, _otherProjectId, _resourceId);
            seedContext.Set<ProjectCommandTemplate>().Add(otherProjectTemplate);
            seedContext.Set<ProjectCommandTemplatePartition>().Add(new ProjectCommandTemplatePartition
            {
                Id = Guid.NewGuid(),
                State = ECommandTemplateState.Active,
                CommandTemplateId = otherProjectTemplateId,
                LocationPartitionId = Guid.NewGuid(),
                ProjectCommandTemplate = otherProjectTemplate
            });

            if (seedMixedStates)
            {
                var activeTemplate = ProjectCommandTemplate.Create("active-template", ECommandTemplateType.Unknown, _projectId, _resourceId);
                seedContext.Set<ProjectCommandTemplate>().Add(activeTemplate);
                var locationPartition = new LocationPartition
                {
                    Id = Guid.NewGuid(),
                    Name = "queue-a",
                    LocationId = Guid.NewGuid()
                };
                seedContext.Set<LocationPartition>().Add(locationPartition);
                seedContext.Set<ProjectCommandTemplatePartition>().Add(new ProjectCommandTemplatePartition
                {
                    Id = Guid.NewGuid(),
                    State = ECommandTemplateState.Active,
                    CommandTemplateId = activeTemplateId,
                    LocationPartitionId = locationPartition.Id,
                    LocationPartition = locationPartition,
                    ProjectCommandTemplate = activeTemplate
                });
            }

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

        var service = new HeappeService(
            mockLogger.Object,
            mockHttpClientFactory.Object,
            mockFeatureManager.Object,
            mockDbContextFactory.Object,
            new KillSwitchStateService(new HeappeIntegrationDbContext(options)),
            mockVersionResolver.Object,
            Options.Create(new ClusterAuthTypeOptions()));

        return (service, activeTemplateId);
    }
}
