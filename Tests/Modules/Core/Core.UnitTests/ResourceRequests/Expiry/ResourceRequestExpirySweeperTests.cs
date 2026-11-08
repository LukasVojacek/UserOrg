using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest.Expiry;
using Core.Features.ResourceRequest.Flow;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.UnitTests.ResourceRequests.Expiry;

/// <summary>
/// Unit tests for <see cref="ResourceRequestExpirySweeper"/> (DEC-7/DER-2/DER-3): a poll-loop background service
/// that reaps requests parked past their configured expiry in either gate by resuming the existing cancel signal
/// path with the <c>Expire</c> flag, converging on <c>InvalidateLinks</c> rather than a second termination path.
/// <c>SweepOnceAsync</c> is internal (not private) purely as a test seam, mirroring
/// <c>OutboxDispatcherService.PollOnceAsync</c>.
/// </summary>
public sealed class ResourceRequestExpirySweeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
    private const int DurationInDays = 30;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ResourceRequestDetail Detail(string slug, string state, Guid workflowId) => new(
        Slug: slug,
        State: state,
        ProjectShortName: "proj",
        ProviderName: "acme",
        ResourceName: "gpu",
        RequesterIdpId: "idp-1",
        ContactEmail: "pi@example.org",
        WorkflowRequestId: workflowId,
        Reason: null,
        TokenExpiresAt: null,
        CreatedAt: Now.AddDays(-40),
        UpdatedAt: Now.AddDays(-31));

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private static (ResourceRequestExpirySweeper Sweeper, Mock<IWorkflowClient> Workflow, Mock<IResourceRequestProjectionStore> Store, CapturingLogger<ResourceRequestExpirySweeper> Logger, ServiceProvider Provider) BuildSweeper(
        IReadOnlyList<ResourceRequestDetail> due)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query
            .Setup(q => q.FindDueForExpiryAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(due);

        var workflow = new Mock<IWorkflowClient>();
        workflow
            .Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.Resumed));
        workflow
            .Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.Resumed));

        var store = new Mock<IResourceRequestProjectionStore>();

        var services = new ServiceCollection();
        services.AddScoped(_ => query.Object);
        services.AddScoped(_ => workflow.Object);
        services.AddScoped(_ => store.Object);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var timeProvider = new FixedTimeProvider(Now);
        var options = Options.Create(new ResourceRequestExpiryOptions { DurationInDays = DurationInDays });
        var logger = new CapturingLogger<ResourceRequestExpirySweeper>();
        var sweeper = new ResourceRequestExpirySweeper(scopeFactory, options, timeProvider, logger);

        return (sweeper, workflow, store, logger, provider);
    }

    /// <summary>Makes the row's resume come back <c>NotAwaiting</c> with the engine status the branch table reads.</summary>
    private static void ArmNotAwaiting(Mock<IWorkflowClient> workflow, Guid workflowId, RequestStatus? status)
    {
        workflow
            .Setup(w => w.ResumeAsync(workflowId, It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.NotAwaiting));
        workflow
            .Setup(w => w.GetStatusAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
    }

    /// <summary>Replays the mutation the sweeper handed to the store onto a row, so the write can be asserted.</summary>
    private static ProjectResourceRequest CapturedWrite(Mock<IResourceRequestProjectionStore> store, string slug)
    {
        var row = new ProjectResourceRequest
        {
            State = ResourceRequestState.PendingPiApproval,
            TokenHash = "hash",
            TokenExpiresAt = Now.AddDays(1),
        };

        store.Verify(
            s => s.UpdateBySlugAsync(slug, It.IsAny<Action<ProjectResourceRequest>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var mutate = store.Invocations
            .Single(i => i.Method.Name == nameof(IResourceRequestProjectionStore.UpdateBySlugAsync))
            .Arguments[1] as Action<ProjectResourceRequest>;
        mutate!(row);

        return row;
    }

    [Fact]
    public async Task Should_ResumePiDecisionExpire_When_RowPastCutoffInPendingPiApproval()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, _, _, provider) = BuildSweeper([Detail("proj-gpu", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;

        await sweeper.SweepOnceAsync(ct);

        workflow.Verify(w => w.ResumeAsync(
            workflowId, It.Is<PiDecision>(d => d.Decision == PiDecisionKind.Expire), ct), Times.Once);
        workflow.Verify(w => w.ResumeAsync(
            It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ResumeResourceAvailabilityExpire_When_RowPastCutoffInAwaitingResourceCreation()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, _, _, provider) = BuildSweeper([Detail("proj-gpu", "AwaitingResourceCreation", workflowId)]);
        using var providerScope = provider;

        await sweeper.SweepOnceAsync(ct);

        workflow.Verify(w => w.ResumeAsync(
            workflowId, It.Is<ResourceAvailabilitySignal>(s => s.Signal == ResourceAvailabilityKind.Expire), ct), Times.Once);
        workflow.Verify(w => w.ResumeAsync(
            It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_QueryWithConfiguredDurationBeforeNow_When_Sweeping()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = new Mock<IProjectResourceRequestQuery>();
        query
            .Setup(q => q.FindDueForExpiryAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var services = new ServiceCollection();
        services.AddScoped(_ => query.Object);
        services.AddScoped(_ => new Mock<IWorkflowClient>().Object);
        services.AddScoped(_ => new Mock<IResourceRequestProjectionStore>().Object);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var timeProvider = new FixedTimeProvider(Now);
        var options = Options.Create(new ResourceRequestExpiryOptions { DurationInDays = DurationInDays });
        using var sweeper = new ResourceRequestExpirySweeper(
            scopeFactory, options, timeProvider, NullLogger<ResourceRequestExpirySweeper>.Instance);

        await sweeper.SweepOnceAsync(ct);

        query.Verify(q => q.FindDueForExpiryAsync(Now.AddDays(-DurationInDays), ct), Times.Once);
    }

    [Fact]
    public async Task Should_ContinueSweep_When_OneRowFailsToResume()
    {
        var ct = TestContext.Current.CancellationToken;
        var failing = Guid.CreateVersion7();
        var succeeding = Guid.CreateVersion7();
        var (sweeper, workflow, _, _, provider) = BuildSweeper([
            Detail("proj-fails", "PendingPiApproval", failing),
            Detail("proj-ok", "PendingPiApproval", succeeding),
        ]);
        using var providerScope = provider;
        workflow
            .Setup(w => w.ResumeAsync(failing, It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await sweeper.SweepOnceAsync(ct);

        workflow.Verify(w => w.ResumeAsync(
            succeeding, It.Is<PiDecision>(d => d.Decision == PiDecisionKind.Expire), ct), Times.Once);
    }

    [Theory]
    [InlineData("PendingPiApproval")]
    [InlineData("AwaitingResourceCreation")]
    public async Task Should_SettleRowAsExpired_When_RowHasNoWorkflowToResume(string state)
    {
        var ct = TestContext.Current.CancellationToken;
        var (sweeper, workflow, store, _, provider) = BuildSweeper([Detail("proj-orphan", state, Guid.Empty)]);
        using var providerScope = provider;

        await sweeper.SweepOnceAsync(ct);

        var row = CapturedWrite(store, "proj-orphan");
        Assert.Equal(ResourceRequestState.Expired, row.State);
        Assert.True(row.State.IsTerminal, "a terminal row is outside the filtered single-active index");
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
        workflow.Verify(w => w.ResumeAsync(
            It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()), Times.Never);
        workflow.Verify(w => w.ResumeAsync(
            It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_SettleRowAsExpired_When_EngineHasNoSuchRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, _, provider) = BuildSweeper([Detail("proj-lost", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        workflow
            .Setup(w => w.ResumeAsync(workflowId, It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(ResumeOutcome.RequestNotFound));

        await sweeper.SweepOnceAsync(ct);

        Assert.Equal(ResourceRequestState.Expired, CapturedWrite(store, "proj-lost").State);
    }

    [Fact]
    public async Task Should_SettleRowAsFailed_When_EngineRequestFailedUnrecoverably()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, _, provider) = BuildSweeper([Detail("proj-dead", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, RequestStatus.Failed);

        await sweeper.SweepOnceAsync(ct);

        var row = CapturedWrite(store, "proj-dead");
        Assert.Equal(ResourceRequestState.Failed, row.State);
        Assert.True(row.State.IsTerminal, "a terminal row is outside the filtered single-active index");
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
    }

    [Fact]
    public async Task Should_SettleRowAsExpired_When_EngineDoesNotKnowTheRequestStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, _, provider) = BuildSweeper([Detail("proj-unknown", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, status: null);

        await sweeper.SweepOnceAsync(ct);

        Assert.Equal(ResourceRequestState.Expired, CapturedWrite(store, "proj-unknown").State);
    }

    [Theory]
    [InlineData(RequestStatus.Accepted)]
    [InlineData(RequestStatus.InProgress)]
    public async Task Should_LeaveRowAndReportStalledEngineRequest_When_EngineRequestIsStillAlive(RequestStatus status)
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, logger, provider) = BuildSweeper([Detail("proj-stalled", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, status);

        await sweeper.SweepOnceAsync(ct);

        AssertRowUntouched(store);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("stalled", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(status.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_LeaveRowSilently_When_EngineCompletedAndTheRowIsAlreadyTerminal()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, logger, provider) = BuildSweeper([Detail("proj-raced", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, RequestStatus.Completed);
        store
            .Setup(s => s.GetBySlugAsync("proj-raced", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectResourceRequest { State = ResourceRequestState.Assigned });

        await sweeper.SweepOnceAsync(ct);

        AssertRowUntouched(store);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("proj-raced", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportAnomaly_When_EngineCompletedButTheFreshRowIsStillNonTerminal()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, logger, provider) = BuildSweeper([Detail("proj-unwritten", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, RequestStatus.Completed);
        store
            .Setup(s => s.GetBySlugAsync("proj-unwritten", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectResourceRequest { State = ResourceRequestState.PendingPiApproval });

        await sweeper.SweepOnceAsync(ct);

        AssertRowUntouched(store);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("proj-unwritten", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportDivergence_When_TheRequestIsAwaitingASignalTheEngineCannotRoute()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, logger, provider) = BuildSweeper([Detail("proj-diverged", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, RequestStatus.AwaitingSignal);

        await sweeper.SweepOnceAsync(ct);

        // Warning, not error: this same observation also follows a benign race (the request parked between
        // ResumeAsync and GetStatusAsync), which nothing here can tell apart from a genuine divergence.
        AssertRowUntouched(store);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("proj-diverged", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_NotClobberALegitimateTerminalWrite_When_TheFlowSettledTheRowFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, store, _, provider) = BuildSweeper([Detail("proj-contended", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, RequestStatus.Failed);

        await sweeper.SweepOnceAsync(ct);

        // The store applies the mutation to the row as it stands at write time; simulate the flow having settled
        // it to Assigned in the meantime by replaying the mutation on an already-terminal row.
        var mutate = (Action<ProjectResourceRequest>)store.Invocations
            .Single(i => i.Method.Name == nameof(IResourceRequestProjectionStore.UpdateBySlugAsync))
            .Arguments[1];
        var settledByTheFlow = new ProjectResourceRequest
        {
            State = ResourceRequestState.Assigned,
            TokenHash = "flow-issued-hash",
            TokenExpiresAt = Now.AddDays(1),
        };

        mutate(settledByTheFlow);

        Assert.Equal(ResourceRequestState.Assigned, settledByTheFlow.State);
        Assert.Equal("flow-issued-hash", settledByTheFlow.TokenHash);
        Assert.Equal(Now.AddDays(1), settledByTheFlow.TokenExpiresAt);
    }

    [Fact]
    public async Task Should_AskTheEngineForStatus_When_ResumeReturnedNotAwaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, _, _, provider) = BuildSweeper([Detail("proj-stalled", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        ArmNotAwaiting(workflow, workflowId, RequestStatus.InProgress);

        await sweeper.SweepOnceAsync(ct);

        workflow.Verify(
            w => w.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ResumeOutcome.Resumed)]
    [InlineData(ResumeOutcome.RequestNotFound)]
    [InlineData(ResumeOutcome.SignalTypeMismatch)]
    public async Task Should_NotAskTheEngineForStatus_When_ResumeReturnedAnythingElse(ResumeOutcome outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.CreateVersion7();
        var (sweeper, workflow, _, _, provider) = BuildSweeper([Detail("proj-gpu", "PendingPiApproval", workflowId)]);
        using var providerScope = provider;
        workflow
            .Setup(w => w.ResumeAsync(workflowId, It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));

        await sweeper.SweepOnceAsync(ct);

        workflow.Verify(
            w => w.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_NotAskTheEngineForStatus_When_TheRowHasNoWorkflowToResume()
    {
        var ct = TestContext.Current.CancellationToken;
        var (sweeper, workflow, _, _, provider) = BuildSweeper([Detail("proj-orphan", "PendingPiApproval", Guid.Empty)]);
        using var providerScope = provider;

        await sweeper.SweepOnceAsync(ct);

        workflow.Verify(
            w => w.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertRowUntouched(Mock<IResourceRequestProjectionStore> store) =>
        store.Verify(
            s => s.UpdateBySlugAsync(It.IsAny<string>(), It.IsAny<Action<ProjectResourceRequest>>(), It.IsAny<CancellationToken>()),
            Times.Never);
}
