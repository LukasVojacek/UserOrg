using Core.Features.Project.AssignProjectResource;
using Core.Features.Provider.ResourceAssignment;

using Moq;

namespace Core.UnitTests.Projects;

/// <summary>
/// Unit test for <see cref="ProjectResourceSyncHandler"/> (F5 Phase E): the post-commit handler of
/// <see cref="ProjectResourceAssignmentChangedEvent"/> enqueues exactly one HEAppE sync for the single
/// affected project through the <see cref="IResourceSyncRunner"/> port. Mirrors
/// <see cref="Providers.ResourceAssignmentEventHandlerTests"/>; the port is mocked at the boundary.
/// </summary>
public sealed class ProjectResourceSyncHandlerTests
{
    [Fact]
    public async Task Should_TriggerSyncForAffectedProject_When_AssignmentChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var shortName = new ProjectShortName("proj-a");
        var resourceName = ResourceName.Create("gpu-res");
        var runner = new Mock<IResourceSyncRunner>();

        var handler = new ProjectResourceSyncHandler(runner.Object);
        await handler.HandleAsync(new ProjectResourceAssignmentChangedEvent(shortName, resourceName), ct);

        runner.Verify(r => r.RunResourceSyncAsync(shortName, resourceName, It.IsAny<CancellationToken>()), Times.Once);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Propagate_When_RunnerFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new Mock<IResourceSyncRunner>();
        runner.Setup(r => r.RunResourceSyncAsync(It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sync failed"));

        var handler = new ProjectResourceSyncHandler(runner.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new ProjectResourceAssignmentChangedEvent(new ProjectShortName("proj-a"), ResourceName.Create("gpu-res")), ct));
    }
}
