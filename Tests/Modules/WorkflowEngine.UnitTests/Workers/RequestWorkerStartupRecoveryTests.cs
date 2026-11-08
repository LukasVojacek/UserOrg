using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// Startup recovery is the only thing standing between a lost queue message and a request that hangs
/// forever: the queue is an in-process channel, so the durability boundary is the <c>workflow.Requests</c>
/// row, not the channel. These tests pin which persisted rows recovery must pick up — and, just as
/// importantly, which it must leave alone.
/// <para>
/// The repository under the recovery is the production <c>WorkflowDbContextRepository</c> over a real
/// relational provider (SQLite). ADR-0067 records why: recovery is a predicate over persisted rows, and a
/// fake repository will happily agree with whatever predicate is written above it.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class RequestWorkerStartupRecoveryTests : IAsyncLifetime
{
    private const string FlowName = "recovery-queue-loss";

    private readonly RecordingQueue _queue = new();
    private readonly CapturingLogger<RequestWorkerStartupRecovery> _recoveryLogs = new();
    private SqliteWorkflowDatabase _database = null!;
    private ServiceProvider _provider = null!;

    // ── Flow under test: StepA ─► StepB ─► done ──────────────────────────────────

    private sealed record StepA(Guid RequestId) : StepRequest(RequestId, nameof(StepA));

    private sealed record StepB(Guid RequestId) : StepRequest(RequestId, nameof(StepB));

    // A message type System.Text.Json refuses to (de)serialize — an nint member — so replaying a persisted
    // row that names this type throws NotSupportedException, not JsonException or InvalidOperationException.
    private sealed record Unsupported(Guid RequestId, nint Bogus) : StepRequest(RequestId, nameof(Unsupported));

    private sealed class NoopHandler<T> : IStepHandler<T> where T : StepRequest
    {
        public Task<StepResult> HandleAsync(T request, CancellationToken ct) => Task.FromResult(new StepResult(null));
    }

    private sealed class TwoStepFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named(FlowName)
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .From<StepA>().GoTo((_, _, snap) => new StepB(snap.RequestId))
            .From<StepB>().Terminal();
    }

    private const string ThrowingFlowName = "recovery-transition-throws";

    // Distinct step types from TwoStepFlow's — StrategyRegistryImpl keys strategies by InitialStepType, so
    // reusing StepA/StepB here would collide with TwoStepFlow's registration.
    private sealed record ThrowStepA(Guid RequestId) : StepRequest(RequestId, nameof(ThrowStepA));

    private sealed record ThrowStepB(Guid RequestId) : StepRequest(RequestId, nameof(ThrowStepB));

    // A registered strategy whose Transition — flow-author code, not strategy resolution — throws
    // InvalidOperationException. Proves that exception is not misreported as "no strategy registered".
    private sealed class ThrowingFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new ThrowStepA(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named(ThrowingFlowName)
            .Step<ThrowStepA, NoopHandler<ThrowStepA>>()
            .Step<ThrowStepB, NoopHandler<ThrowStepB>>()
            .From<ThrowStepA>().GoTo<ThrowStepB>((_, _, _) =>
                throw new InvalidOperationException("flow-author bug, unrelated to strategy resolution"))
            .From<ThrowStepB>().Terminal();
    }

    /// <summary>Minimal <see cref="ILogger{T}"/> capturing rendered messages, for asserting which of two
    /// InvalidOperationException-triggered log messages was actually emitted.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class NoopCallbackPort : ICallbackPort
    {
        public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
    }

    public async ValueTask InitializeAsync()
    {
        _database = await SqliteWorkflowDatabase.CreateAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<WorkflowDbContext>>(_database);
        services.AddScoped<IRequestRepository, WorkflowDbContextRepository>();
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<TwoStepFlow>();
        services.AddFlow<ThrowingFlow>();

        // Registered last so it wins over the channel adapter: everything the engine enqueues is recorded.
        services.AddSingleton<IQueuePort>(_queue);

        // Registered last so it wins over the default ILogger<T> factory registration: recovery's log
        // messages are captured for assertion.
        services.AddSingleton<ILogger<RequestWorkerStartupRecovery>>(_recoveryLogs);

        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact(DisplayName = "Recovery re-enqueues an Accepted request that never got a step row")]
    public async Task Should_ReEnqueueAcceptedRequestsWithNoStepRow_When_RecoveryRuns()
    {
        var ct = TestContext.Current.CancellationToken;

        Guid requestId;
        await using (var scope = _provider.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
            requestId = await client.StartAsync<TwoStepFlow>(new object(), ct: ct);
        }

        // Precondition: RequestAggregate.Create adds no step, so the durable row is Accepted and stepless.
        var submitted = await FindAsync(requestId);
        Assert.Equal(RequestStatus.Accepted, submitted.Status);
        Assert.Empty(submitted.Steps);

        // The process died between the commit and the consume; the in-memory channel went with it.
        _queue.Clear();

        await Recovery().StartAsync(ct);

        var message = Assert.Single(_queue.Messages);
        Assert.Equal(requestId, message.RequestId);
        Assert.Equal(nameof(StepA), message.StateName);
        Assert.IsType<StepA>(message);
    }

    [Fact(DisplayName = "Recovery re-enqueues an InProgress request whose steps are all finished")]
    public async Task Should_ReEnqueueInProgressRequestsWithNoRunningStep_When_RecoveryRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.CreateVersion7();
        var aggregate = NewRequest(requestId, now);

        // The shape a lost enqueue leaves behind: the flow has been advanced to StepB and saved, but the
        // message for StepB never reached a worker, so no StepB row exists and nothing is Running. This is
        // also the shape a gate resume leaves behind (WorkflowClient.cs:124 saves, :134 enqueues).
        aggregate.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(requestId)), typeof(StepA).AssemblyQualifiedName!, now);
        aggregate.RecordStepCompleted(nameof(StepA), null, now);
        aggregate.SetCurrentState(nameof(StepB), now);

        await SeedAsync(aggregate);

        var stranded = await FindAsync(requestId);
        Assert.Equal(RequestStatus.InProgress, stranded.Status);
        Assert.DoesNotContain(stranded.Steps, s => s.ExecutionStatus == StepExecutionStatus.Running);

        await Recovery().StartAsync(ct);

        var message = Assert.Single(_queue.Messages);
        Assert.Equal(requestId, message.RequestId);

        // The durable row says the request is AT StepB. Re-enqueueing the already-completed StepA and
        // leaning on the worker's replay guard would look equivalent here but does not recover a gate
        // resume, where replaying the last completed step routes back into the gate.
        Assert.Equal(nameof(StepB), message.StateName);
    }

    // ── What recovery must keep doing, and what it must not start doing ──────────

    [Fact(DisplayName = "Recovery re-enqueues a Running step whose lease has expired")]
    public async Task Should_ReEnqueueStalledStep_When_RunningLeaseHasExpired()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.CreateVersion7();
        var aggregate = NewRequest(requestId, now);

        aggregate.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(requestId)), typeof(StepA).AssemblyQualifiedName!, now);
        var step = aggregate.Steps.Single();
        step.LockedBy = "worker-that-crashed";
        step.LockedUntil = now.AddMinutes(-10);

        await SeedAsync(aggregate);

        await Recovery().StartAsync(ct);

        var message = Assert.Single(_queue.Messages);
        Assert.Equal(requestId, message.RequestId);
        Assert.Equal(nameof(StepA), message.StateName);

        var reset = await FindAsync(requestId);
        Assert.Equal(StepExecutionStatus.Running, reset.Steps.Single().ExecutionStatus);
        Assert.Null(reset.Steps.Single().LockedUntil);
    }

    [Fact(DisplayName = "Recovery leaves a Running step alone while its lease is still live")]
    public async Task Should_NotReEnqueueAnything_When_RunningStepLeaseIsStillLive()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.CreateVersion7();
        var aggregate = NewRequest(requestId, now);

        aggregate.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(requestId)), typeof(StepA).AssemblyQualifiedName!, now);
        var step = aggregate.Steps.Single();
        step.LockedBy = "worker-alive";
        step.LockedUntil = now.AddMinutes(10);

        await SeedAsync(aggregate);

        await Recovery().StartAsync(ct);

        Assert.Empty(_queue.Messages);
    }

    [Fact(DisplayName = "Recovery leaves a request parked at a gate alone")]
    public async Task Should_NotReEnqueueAnything_When_RequestIsAwaitingSignal()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.CreateVersion7();
        var aggregate = NewRequest(requestId, now);

        // A parked request also has no Running step. It is waiting for a human, not for a message.
        aggregate.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(requestId)), typeof(StepA).AssemblyQualifiedName!, now);
        aggregate.RecordStepCompleted(nameof(StepA), null, now);
        aggregate.MarkAwaitingSignal("ApprovalGate", now);

        await SeedAsync(aggregate);

        await Recovery().StartAsync(ct);

        Assert.Empty(_queue.Messages);
    }

    [Fact(DisplayName = "Recovery leaves a finished request alone")]
    public async Task Should_NotReEnqueueAnything_When_RequestIsCompleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.CreateVersion7();
        var aggregate = NewRequest(requestId, now);

        aggregate.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(requestId)), typeof(StepA).AssemblyQualifiedName!, now);
        aggregate.RecordStepCompleted(nameof(StepA), null, now);
        aggregate.MarkCompleted(null, now);

        await SeedAsync(aggregate);

        await Recovery().StartAsync(ct);

        Assert.Empty(_queue.Messages);
    }

    [Fact(DisplayName = "Recovery leaves a step waiting on its retry back-off alone")]
    public async Task Should_NotReEnqueueAnything_When_StepIsRetryScheduled()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var requestId = Guid.CreateVersion7();
        var aggregate = NewRequest(requestId, now);

        // RetryScheduled is InProgress with no Running step too — the trap in the widened predicate. This
        // step belongs to RetryScheduledScanner, which re-enqueues it when NextRetryAt falls due; the one
        // duplicate the worker cannot absorb is exactly this one, because it re-executes immediately and
        // skips the back-off.
        aggregate.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(requestId)), typeof(StepA).AssemblyQualifiedName!, now);
        aggregate.RecordStepRetryScheduled(
            nameof(StepA), new ErrorInfo("TRANSIENT", "upstream was busy"), now.AddMinutes(5), now);

        await SeedAsync(aggregate);

        var waiting = await FindAsync(requestId);
        Assert.Equal(RequestStatus.InProgress, waiting.Status);
        Assert.DoesNotContain(waiting.Steps, s => s.ExecutionStatus == StepExecutionStatus.Running);

        await Recovery().StartAsync(ct);

        Assert.Empty(_queue.Messages);
    }

    [Fact(DisplayName = "A stranded row naming an unknown strategy is skipped, not fatal, and does not block the rest")]
    public async Task Should_SkipTheRowAndKeepBooting_When_AStrandedRequestNamesAnUnknownStrategy()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        // A row whose Strategy has since been renamed or removed — ReconstructNextStep's Resolve must not
        // escape StartAsync for it.
        var unresolvableRequestId = Guid.CreateVersion7();
        var unresolvable = NewRequest(unresolvableRequestId, now, strategy: "strategy-that-no-longer-exists");
        unresolvable.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(unresolvableRequestId)), typeof(StepA).AssemblyQualifiedName!, now);
        unresolvable.RecordStepCompleted(nameof(StepA), null, now);
        unresolvable.SetCurrentState(nameof(StepB), now);
        await SeedAsync(unresolvable);

        // A perfectly recoverable row alongside it — proves the bad row does not abort recovery for the rest.
        var recoverableRequestId = Guid.CreateVersion7();
        var recoverable = NewRequest(recoverableRequestId, now);
        recoverable.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(recoverableRequestId)), typeof(StepA).AssemblyQualifiedName!, now);
        recoverable.RecordStepCompleted(nameof(StepA), null, now);
        recoverable.SetCurrentState(nameof(StepB), now);
        await SeedAsync(recoverable);

        // StartAsync must complete without throwing — an IHostedService that throws here takes the host down.
        await Recovery().StartAsync(ct);

        var message = Assert.Single(_queue.Messages);
        Assert.Equal(recoverableRequestId, message.RequestId);
        Assert.Equal(nameof(StepB), message.StateName);

        // The message is specific to Resolve failing, and must actually be emitted for that case.
        Assert.Contains(_recoveryLogs.Messages, m => m.Contains("no strategy registered", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "A stranded row whose Transition throws an unrelated InvalidOperationException is skipped without being misreported as an unknown strategy")]
    public async Task Should_SkipTheRowAndKeepBooting_When_AStrandedRequestTransitionThrowsAnUnrelatedInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        // The strategy IS registered; the InvalidOperationException comes from Transition (flow-author code),
        // not from _registry.Resolve — it must not be logged as "no strategy registered".
        var throwingRequestId = Guid.CreateVersion7();
        var throwing = NewRequest(throwingRequestId, now, strategy: ThrowingFlowName);
        throwing.RecordStepStarted(
            nameof(ThrowStepA), Serialize(new ThrowStepA(throwingRequestId)),
            typeof(ThrowStepA).AssemblyQualifiedName!, now);
        throwing.RecordStepCompleted(nameof(ThrowStepA), null, now);
        throwing.SetCurrentState(nameof(ThrowStepB), now);
        await SeedAsync(throwing);

        // A perfectly recoverable row alongside it — proves the bad row does not abort recovery for the rest.
        var recoverableRequestId = Guid.CreateVersion7();
        var recoverable = NewRequest(recoverableRequestId, now);
        recoverable.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(recoverableRequestId)), typeof(StepA).AssemblyQualifiedName!, now);
        recoverable.RecordStepCompleted(nameof(StepA), null, now);
        recoverable.SetCurrentState(nameof(StepB), now);
        await SeedAsync(recoverable);

        // StartAsync must complete without throwing — an IHostedService that throws here takes the host down.
        await Recovery().StartAsync(ct);

        var message = Assert.Single(_queue.Messages);
        Assert.Equal(recoverableRequestId, message.RequestId);
        Assert.Equal(nameof(StepB), message.StateName);

        Assert.DoesNotContain(
            _recoveryLogs.Messages, m => m.Contains("no strategy registered", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "A stranded row whose payload cannot be deserialized is skipped, not fatal, and does not block the rest")]
    public async Task Should_KeepBooting_When_AStrandedRequestPayloadCannotBeDeserialized()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        // A row whose persisted InputData throws something other than JsonException or InvalidOperationException
        // when replayed — here NotSupportedException, from a member type (nint) System.Text.Json refuses to
        // (de)serialize. ReconstructNextStep's Resolve/Transition path must not let this escape StartAsync.
        var malformedRequestId = Guid.CreateVersion7();
        var malformed = NewRequest(malformedRequestId, now);
        var malformedInputData = JsonDocument.Parse($$"""{"RequestId":"{{malformedRequestId}}","Bogus":1}""");
        malformed.RecordStepStarted(
            nameof(Unsupported), malformedInputData, typeof(Unsupported).AssemblyQualifiedName!, now);
        malformed.RecordStepCompleted(nameof(Unsupported), null, now);
        malformed.SetCurrentState(nameof(StepB), now);
        await SeedAsync(malformed);

        // A perfectly recoverable row alongside it — proves the bad row does not abort recovery for the rest.
        var recoverableRequestId = Guid.CreateVersion7();
        var recoverable = NewRequest(recoverableRequestId, now);
        recoverable.RecordStepStarted(
            nameof(StepA), Serialize(new StepA(recoverableRequestId)), typeof(StepA).AssemblyQualifiedName!, now);
        recoverable.RecordStepCompleted(nameof(StepA), null, now);
        recoverable.SetCurrentState(nameof(StepB), now);
        await SeedAsync(recoverable);

        // StartAsync must complete without throwing — an IHostedService that throws here takes the host down.
        await Recovery().StartAsync(ct);

        var message = Assert.Single(_queue.Messages);
        Assert.Equal(recoverableRequestId, message.RequestId);
        Assert.Equal(nameof(StepB), message.StateName);
    }

    [Fact(DisplayName = "Startup recovery stays one-shot and registered before the request worker")]
    public void Should_RegisterRecoveryAsOneShotHostedServiceBeforeWorker_When_WorkflowEngineRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowEngineApplicationServices();

        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        var recoveryIndex = hosted.IndexOf(typeof(RequestWorkerStartupRecovery));
        var workerIndex = hosted.IndexOf(typeof(RequestWorker));

        Assert.True(recoveryIndex >= 0, "RequestWorkerStartupRecovery must be registered as an IHostedService.");
        Assert.True(workerIndex >= 0, "RequestWorker must be registered as an IHostedService.");

        // Ordering is the whole safety argument for the two non-lease-gated classes above: recovery must
        // finish its scan before the worker can consume, or it would race the very steps it re-enqueues.
        Assert.True(
            recoveryIndex < workerIndex,
            "Startup recovery must be registered before RequestWorker so its scan completes before the worker consumes.");

        // And it must never become periodic. "InProgress with no Running step" is also the NORMAL in-flight
        // state — between RequestWorker.cs:172 and the next RecordStepStarted, and between
        // WorkflowClient.cs:124 and :134 — so a looping scan would duplicate live steps. A plain
        // IHostedService whose StartAsync runs once is what makes those classes safe to re-enqueue at all.
        Assert.False(
            typeof(BackgroundService).IsAssignableFrom(typeof(RequestWorkerStartupRecovery)),
            "Startup recovery must stay a one-shot IHostedService, never a looping BackgroundService.");
        Assert.True(typeof(BackgroundService).IsAssignableFrom(typeof(RequestWorker)));
    }

    private RequestWorkerStartupRecovery Recovery() => _provider
        .GetServices<IHostedService>()
        .OfType<RequestWorkerStartupRecovery>()
        .Single();

    private static RequestAggregate NewRequest(Guid requestId, DateTimeOffset now, string? strategy = null) => RequestAggregate.Create(
        strategy ?? FlowName,
        Serialize(new StepA(requestId)),
        clientId: null,
        callbackUrl: null,
        idempotencyKey: null,
        now: now,
        requestId: requestId);

    private static JsonDocument Serialize(StepRequest message)
        => JsonSerializer.SerializeToDocument(message, message.GetType());

    private async Task SeedAsync(RequestAggregate aggregate)
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRequestRepository>();
        await repository.AddAsync(aggregate, TestContext.Current.CancellationToken);
    }

    private async Task<RequestAggregate> FindAsync(Guid requestId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRequestRepository>();
        var aggregate = await repository.FindByIdAsync(requestId, TestContext.Current.CancellationToken);
        return Assert.IsType<RequestAggregate>(aggregate);
    }
}
