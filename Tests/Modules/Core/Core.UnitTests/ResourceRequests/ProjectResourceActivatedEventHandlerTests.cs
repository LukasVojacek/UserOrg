using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;
using Core.Features.ResourceRequest.Activation;
using Core.Features.ResourceRequest.Flow;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Tests for <see cref="ProjectResourceActivatedEventHandler"/> (DEC-4): the durable, outbox-dispatched
/// handler that, on a resource's creation, resumes every assignment request parked in
/// <c>AwaitingResourceCreation</c> for that resource key via <c>ResumeAsync(ResourceAvailabilitySignal.Available)</c>.
/// A "not awaiting signal" outcome is transient (the engine may not have parked the gate yet) and MUST throw so
/// the durable event retries; already-settled rows are simply absent from the query and never touched.
/// </summary>
public sealed class ProjectResourceActivatedEventHandlerTests
{
    private const string ProviderName = "acme";
    private const string ResourceName = "gpu-1";

    private static ProjectResourceActivatedEvent Event() =>
        new(new ProjectResourceKey(
            Core.Domain.Provider.ProviderName.From(ProviderName),
            Core.Domain.Provider.ResourceName.From(ResourceName)));

    private static ResourceRequestDetail Row(Guid requestId) => new(
        Slug: $"proj-{requestId:N}", State: "AwaitingResourceCreation", ProjectShortName: "proj",
        ProviderName: ProviderName, ResourceName: ResourceName, RequesterIdpId: "sub", ContactEmail: "pi@example.com",
        WorkflowRequestId: requestId, Reason: null, TokenExpiresAt: null,
        CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

    private static (Mock<IProjectResourceRequestQuery> Query, Mock<IWorkflowClient> Workflow, ProjectResourceActivatedEventHandler Handler)
        CreateSut(IReadOnlyList<ResourceRequestDetail> awaiting)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindAwaitingByResourceKeyAsync(ProviderName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(awaiting);
        var workflow = new Mock<IWorkflowClient>();
        return (query, workflow, new ProjectResourceActivatedEventHandler(query.Object, workflow.Object, NullLogger<ProjectResourceActivatedEventHandler>.Instance));
    }

    [Fact]
    public async Task Should_ResumeEveryAwaitingRowWithAvailableSignal_When_Activated()
    {
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        var (_, workflow, handler) = CreateSut([Row(r1), Row(r2)]);
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.Resumed));

        await handler.HandleAsync(Event(), CancellationToken.None);

        workflow.Verify(w => w.ResumeAsync(
            r1, It.Is<ResourceAvailabilitySignal>(s => s.Signal == ResourceAvailabilityKind.Available), It.IsAny<CancellationToken>()), Times.Once);
        workflow.Verify(w => w.ResumeAsync(
            r2, It.Is<ResourceAvailabilitySignal>(s => s.Signal == ResourceAvailabilityKind.Available), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_Throw_When_EngineNotYetAwaitingSignal()
    {
        var (_, workflow, handler) = CreateSut([Row(Guid.NewGuid())]);
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.NotAwaiting));

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(Event(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_ResumeSiblingsAndNotThrow_When_OneRowPermanentlyUnresumable()
    {
        // A corrupt/misrouted row (RequestNotFound) must not block resuming the healthy siblings, and must not
        // fail the batch (a retry would never fix it).
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        var (_, workflow, handler) = CreateSut([Row(bad), Row(good)]);
        workflow.Setup(w => w.ResumeAsync(bad, It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.RequestNotFound));
        workflow.Setup(w => w.ResumeAsync(good, It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.Resumed));

        await handler.HandleAsync(Event(), CancellationToken.None);

        workflow.Verify(w => w.ResumeAsync(good, It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ResumeSiblingsThenThrow_When_OneRowNotAwaiting()
    {
        // A transient not-yet-parked row still throws (retry), but its healthy sibling is resumed first.
        var racing = Guid.NewGuid();
        var good = Guid.NewGuid();
        var (_, workflow, handler) = CreateSut([Row(good), Row(racing)]);
        workflow.Setup(w => w.ResumeAsync(good, It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.Resumed));
        workflow.Setup(w => w.ResumeAsync(racing, It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.NotAwaiting));

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(Event(), CancellationToken.None));

        workflow.Verify(w => w.ResumeAsync(good, It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_DoNothing_When_NoAwaitingRows()
    {
        var (_, workflow, handler) = CreateSut([]);

        await handler.HandleAsync(Event(), CancellationToken.None);

        workflow.Verify(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
