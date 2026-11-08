using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Persistence;

/// <summary>
/// Ownership and expiry semantics of the step lease, exercised against the real
/// <see cref="WorkflowDbContextRepository"/> on a relational provider — the only place the
/// <c>ExecuteUpdateAsync</c> behind <c>TryAcquireStepLeaseAsync</c> actually runs.
///
/// These are characterization tests: they record what the lease does today and encode ADR-0067's invariant
/// that the lease is what stops a second worker (and a replay) from re-executing a step. Making a due
/// scheduled retry run must not loosen any of them — in particular, a live lease held by a *different*
/// worker must keep being refused.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StepLeaseOwnershipTests : IAsyncLifetime
{
    private const string StepName = "CallHEAppE";

    private SqliteWorkflowDatabase _db = null!;
    private WorkflowDbContextRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        _repository = _db.CreateRepository();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Should_RecordTheOwnerAndExpiry_When_TheLeaseIsGranted()
    {
        var now = DateTimeOffset.UtcNow;
        var aggregate = await SeedAsync(lockedBy: null, lockedUntil: null);

        var acquired = await _repository.TryAcquireStepLeaseAsync(
            aggregate.RequestId, StepName, "worker-A", TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.True(acquired);

        var step = await ReloadStepAsync(aggregate.RequestId);
        Assert.Equal("worker-A", step.LockedBy);
        Assert.NotNull(step.LockedUntil);
        Assert.True(step.LockedUntil > now, "The granted lease must expire in the future.");
    }

    [Fact]
    public async Task Should_RefuseTheLease_When_AnotherWorkerHoldsALiveLease()
    {
        // ADR-0067's core invariant. The fix for scheduled retries must not weaken this.
        var aggregate = await SeedAsync(
            lockedBy: "worker-A", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        var acquired = await _repository.TryAcquireStepLeaseAsync(
            aggregate.RequestId, StepName, "worker-B", TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.False(acquired);

        var step = await ReloadStepAsync(aggregate.RequestId);
        Assert.Equal("worker-A", step.LockedBy);
    }

    [Fact]
    public async Task Should_GrantTheLease_When_TheHeldLeaseHasExpired()
    {
        // The lease is a liveness guard, not a permanent claim: an expired one is reclaimable by anyone.
        var aggregate = await SeedAsync(
            lockedBy: "worker-A", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        var acquired = await _repository.TryAcquireStepLeaseAsync(
            aggregate.RequestId, StepName, "worker-B", TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.True(acquired);

        var step = await ReloadStepAsync(aggregate.RequestId);
        Assert.Equal("worker-B", step.LockedBy);
    }

    [Fact]
    public async Task Should_GrantTheLease_When_ThePreviousHolderReleasedIt()
    {
        var aggregate = await SeedAsync(
            lockedBy: "worker-A", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await _repository.ReleaseStepLeaseAsync(
            aggregate.RequestId, StepName, TestContext.Current.CancellationToken);

        var released = await ReloadStepAsync(aggregate.RequestId);
        Assert.Null(released.LockedBy);
        Assert.Null(released.LockedUntil);

        var acquired = await _repository.TryAcquireStepLeaseAsync(
            aggregate.RequestId, StepName, "worker-B", TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.True(acquired);
    }

    [Fact]
    public async Task Should_ReturnTheStep_When_ItsScheduledRetryTimeHasPassed()
    {
        var aggregate = await SeedAsync(lockedBy: null, lockedUntil: null);

        var due = await _repository.FindDueRetriesAsync(
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var (dueAggregate, dueStep) = Assert.Single(due);
        Assert.Equal(aggregate.RequestId, dueAggregate.RequestId);
        Assert.Equal(StepName, dueStep.StepName);
    }

    [Fact]
    public async Task Should_NotReturnTheStep_When_ItsScheduledRetryTimeIsStillInTheFuture()
    {
        await SeedAsync(
            lockedBy: null, lockedUntil: null, nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var due = await _repository.FindDueRetriesAsync(
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Empty(due);
    }

    [Fact]
    public async Task Should_StillReturnTheStep_When_ItIsAlreadyLeased()
    {
        // The de-dup that keeps RetryScheduledScanner from flooding the queue is the lease, not the query:
        // a due step stays due until a worker flips its status. Anything that drops the scanner's lease
        // therefore has to replace this de-dup, not just remove it.
        var aggregate = await SeedAsync(
            lockedBy: "worker-A", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        var due = await _repository.FindDueRetriesAsync(
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        var (dueAggregate, _) = Assert.Single(due);
        Assert.Equal(aggregate.RequestId, dueAggregate.RequestId);
    }

    private async Task<RequestAggregate> SeedAsync(
        string? lockedBy,
        DateTimeOffset? lockedUntil,
        DateTimeOffset? nextRetryAt = null)
    {
        var requestId = Guid.CreateVersion7();

        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: StepName,
            executionStatus: StepExecutionStatus.RetryScheduled,
            stepRetries: 1,
            nextRetryAt: nextRetryAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            lockedBy: lockedBy,
            lockedUntil: lockedUntil);

        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            status: RequestStatus.InProgress,
            currentState: StepName,
            steps: [step]);

        await _repository.AddAsync(aggregate, TestContext.Current.CancellationToken);
        return aggregate;
    }

    private async Task<RequestStep> ReloadStepAsync(Guid requestId)
    {
        var reloaded = await _repository.FindByIdAsync(requestId, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        return Assert.Single(reloaded.Steps);
    }
}
