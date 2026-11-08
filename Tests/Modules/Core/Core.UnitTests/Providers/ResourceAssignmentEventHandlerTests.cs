using Core.DataAccess.Queries.Contracts;
using Core.Features.Provider.ResourceAssignment;
using Core.Shared.OutputCache;

using Moq;

namespace Core.UnitTests.Providers;

/// <summary>
/// Unit tests for the two post-commit handlers of <see cref="ResourceAssignmentChangedEvent"/> (ADR-0016):
/// <see cref="ResourceSyncHandler"/> (resolves affected projects and triggers HEAppE sync per project) and
/// <see cref="ResourceCacheEvictionHandler{TEvent}"/> (resolves the resource surrogate and evicts the
/// resource-tagged output cache). The event carries the resource's natural key (ADR-0041 L2.6); both
/// collaborators are mocked at the port boundary.
/// </summary>
public sealed class ResourceAssignmentEventHandlerTests
{
    private static readonly ProjectResourceKey Key = new(ProviderName.From("acme"), ResourceName.From("gpu-res"));

    [Fact]
    public async Task Should_TriggerSyncPerProject_When_ResourceAssignmentChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = ResourceName.Create("gpu-res");
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.GetProjectsUsingResourceAsync(Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ResourceProjectRef(new ProjectShortName("proj-a"), resourceName),
                new ResourceProjectRef(new ProjectShortName("proj-b"), resourceName),
            ]);
        var runner = new Mock<IResourceSyncRunner>();

        var handler = new ResourceSyncHandler(query.Object, runner.Object);
        await handler.HandleAsync(new ResourceAssignmentChangedEvent(Key), ct);

        runner.Verify(r => r.RunResourceSyncAsync(new ProjectShortName("proj-a"), resourceName, It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(r => r.RunResourceSyncAsync(new ProjectShortName("proj-b"), resourceName, It.IsAny<CancellationToken>()), Times.Once);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotTriggerSync_When_NoProjectsUseResource()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.GetProjectsUsingResourceAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var runner = new Mock<IResourceSyncRunner>();

        var handler = new ResourceSyncHandler(query.Object, runner.Object);
        await handler.HandleAsync(new ResourceAssignmentChangedEvent(Key), ct);

        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_EvictResourceTag_When_ResourceAssignmentChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.GetResourceSurrogateAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(id);
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        var handler = new ResourceCacheEvictionHandler<ResourceAssignmentChangedEvent>(cache.Object, query.Object);
        await handler.HandleAsync(new ResourceAssignmentChangedEvent(Key), ct);

        cache.Verify(c => c.EvictAsync($"resource:{id}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_NotEvict_When_ResourceSurrogateUnresolved()
    {
        // A removed/missing resource resolves to null → nothing to evict.
        var ct = TestContext.Current.CancellationToken;
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.GetResourceSurrogateAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        var cache = new Mock<IOutputCacheInvalidator>();

        var handler = new ResourceCacheEvictionHandler<ResourceAssignmentChangedEvent>(cache.Object, query.Object);
        await handler.HandleAsync(new ResourceAssignmentChangedEvent(Key), ct);

        cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
