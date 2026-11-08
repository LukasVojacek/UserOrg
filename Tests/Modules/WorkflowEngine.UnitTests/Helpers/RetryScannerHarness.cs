using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// Runs <see cref="RetryScheduledScanner"/> alone — no <c>RequestWorker</c>, no startup recovery — over a
/// real <see cref="SqliteWorkflowDatabase"/> and the real repository, with a <see cref="RecordingQueue"/> in
/// place of the channel and a <see cref="CapturingLogger{T}"/> in place of the scanner's logger.
///
/// <para>
/// Leaving the worker out is deliberate: these tests are about what the scanner does with a row it can
/// never turn into a message, and a worker would only add a second actor to the same rows. The queue and
/// the log are the scanner's two outputs, so both are recorded; the repository is the real one because the
/// lease decision is an <c>ExecuteUpdateAsync</c> that only a relational provider can run (ADR-0067).
/// </para>
///
/// <para>
/// The polling interval is deliberately tiny (<see cref="PollInterval"/>): the defect under test is
/// "the same row is picked up again on every poll, forever", so a test second has to buy dozens of scans.
/// </para>
/// </summary>
internal sealed class RetryScannerHarness : IAsyncDisposable
{
    /// <summary>The scanner's <c>WorkerId</c> — also the <c>LockedBy</c> value it writes.</summary>
    public const string WorkerId = "scanner-under-test";

    /// <summary>Two strategies, so a test can show the behaviour is not specific to one flow.</summary>
    public const string AlphaStrategy = "poison-alpha";
    public const string BravoStrategy = "poison-bravo";

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly SqliteWorkflowDatabase _db;
    private readonly ServiceProvider _provider;

    public RecordingQueue Queue { get; }

    public CapturingLogger<RetryScheduledScanner> Logs { get; }

    private RetryScannerHarness(
        SqliteWorkflowDatabase db,
        ServiceProvider provider,
        RecordingQueue queue,
        CapturingLogger<RetryScheduledScanner> logs)
    {
        _db = db;
        _provider = provider;
        Queue = queue;
        Logs = logs;
    }

    public static async Task<RetryScannerHarness> CreateAsync(CancellationToken ct = default)
    {
        var db = await SqliteWorkflowDatabase.CreateAsync(ct);
        var queue = new RecordingQueue();
        var logs = new CapturingLogger<RetryScheduledScanner>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db.Factory);
        services.AddScoped<IRequestRepository>(_ => db.CreateRepository());
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<AlphaFlow>();
        services.AddFlow<BravoFlow>();

        // Registered last so they win over the channel adapter and the default logger factory:
        // everything the scanner enqueues and everything it logs is recorded.
        services.AddSingleton<IQueuePort>(queue);
        services.AddSingleton<ILogger<RetryScheduledScanner>>(logs);

        // The options are init-only, so the whole IOptions instance is replaced rather than configured.
        services.AddSingleton<IOptions<WorkflowEngineOptions>>(Options.Create(new WorkflowEngineOptions
        {
            WorkerId = WorkerId,
            LeaseDuration = TimeSpan.FromMinutes(5),
            RetryPollingInterval = PollInterval,
        }));

        // Ownership passes to the harness, which disposes it in DisposeAsync.
#pragma warning disable IDISP001, IDISP004 // the harness is the owner from here on
        var provider = services.BuildServiceProvider();
#pragma warning restore IDISP001, IDISP004

        return new RetryScannerHarness(db, provider, queue, logs);
    }

    /// <summary>Starts the scanner; disposing the result stops it.</summary>
    public async Task<IAsyncDisposable> StartScannerAsync()
    {
        var scanner = _provider.GetServices<IHostedService>().OfType<RetryScheduledScanner>().Single();
        await scanner.StartAsync(CancellationToken.None);
        return new RunningScanner(scanner);
    }

    /// <summary>
    /// Seeds one <c>InProgress</c> request with a single <c>RetryScheduled</c> step. Everything the scanner
    /// reads is a parameter: the message type it resolves, the payload it deserializes, when the retry falls
    /// due and whether somebody else already holds the lease.
    /// </summary>
    public async Task<Guid> SeedRetryScheduledStepAsync(
        string strategy,
        string stepName,
        string messageType,
        JsonDocument? inputData,
        TimeSpan? dueIn = null,
        string? lockedBy = null,
        DateTimeOffset? lockedUntil = null,
        Guid? id = null)
    {
        // The caller may pin the id when the payload has to carry the same one (a healthy message does).
        var requestId = id ?? Guid.CreateVersion7();

        var step = RequestStep.Rehydrate(
            stepId: Guid.CreateVersion7(),
            requestId: requestId,
            stepName: stepName,
            executionStatus: StepExecutionStatus.RetryScheduled,
            messageType: messageType,
            inputData: inputData,
            stepRetries: 1,
            nextRetryAt: DateTimeOffset.UtcNow.Add(dueIn ?? TimeSpan.FromMinutes(-1)),
            lockedBy: lockedBy,
            lockedUntil: lockedUntil,
            startedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            completedAt: null,
            result: null,
            errorCode: "TRANSIENT",
            errorMessage: "HEAppE timed out");

        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            strategy: strategy,
            status: RequestStatus.InProgress,
            currentState: stepName,
            steps: [step]);

        await _db.CreateRepository().AddAsync(aggregate, TestContext.Current.CancellationToken);
        return requestId;
    }

    /// <summary>
    /// Seeds one due <c>RetryScheduled</c> row, carrying its attempt count, for a step the flow is no longer
    /// sitting on: the request is <c>InProgress</c> on <paramref name="advancedState"/> while this row is
    /// still selected by <c>FindDueRetriesAsync</c>.
    ///
    /// <para>
    /// One row, not the stale-plus-superseding pair this helper seeded before T-121. A retried step keeps a
    /// single live row per step name and carries <c>StepRetries</c> forward on it, so a row superseded by a
    /// newer row of the same name is a shape the engine no longer produces. What is left to pin is that the
    /// scanner fails the exact row it was handed and leaves a request making progress elsewhere alone.
    /// </para>
    /// </summary>
    public async Task<Guid> SeedDueRowOnAStepTheFlowHasMovedPastAsync(
        string strategy,
        string stepName,
        string messageType,
        JsonDocument? inputData,
        string advancedState)
    {
        var requestId = Guid.CreateVersion7();

        var due = RequestStep.Rehydrate(
            stepId: Guid.CreateVersion7(),
            requestId: requestId,
            stepName: stepName,
            executionStatus: StepExecutionStatus.RetryScheduled,
            messageType: messageType,
            inputData: inputData,
            stepRetries: 1,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            lockedBy: null,
            lockedUntil: null,
            startedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            completedAt: null,
            result: null,
            errorCode: "TRANSIENT",
            errorMessage: "HEAppE timed out");

        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            strategy: strategy,
            status: RequestStatus.InProgress,
            currentState: advancedState,
            steps: [due]);

        await _db.CreateRepository().AddAsync(aggregate, TestContext.Current.CancellationToken);
        return requestId;
    }

    /// <summary>Reads the persisted aggregate, tolerating the fixture's SQLite write locks.</summary>
    public Task<RequestAggregate?> LoadAsync(Guid requestId) => ReadThroughWriteLocksAsync(
        repository => repository.FindByIdAsync(requestId, TestContext.Current.CancellationToken));

    /// <summary>What <c>FindDueRetriesAsync</c> would hand the scanner right now for this request.</summary>
    public Task<int> CountDueRetriesAsync(Guid requestId) => ReadThroughWriteLocksAsync(async repository =>
    {
        var due = await repository.FindDueRetriesAsync(
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        return due.Count(entry => entry.Aggregate.RequestId == requestId);
    });

    /// <summary>
    /// Runs <paramref name="read"/> against a fresh repository, retrying while SQLite reports the fixture's
    /// own write lock.
    /// </summary>
    private async Task<T> ReadThroughWriteLocksAsync<T>(Func<WorkflowDbContextRepository, Task<T>> read)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return await read(_db.CreateRepository());
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                // 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED: each DbContext holds its own connection to the
                // fixture's shared-cache database, so a read landing mid-write of the scanner's own save
                // hits a SQLite-level write lock — an expected race, not a failure.
                await Task.Delay(PollInterval);
            }
        }

        // Out of retries: let the real exception be the test's failure rather than a silent last value.
        return await read(_db.CreateRepository());
    }

    /// <summary>Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses.</summary>
    public static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(PollInterval);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return await condition();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    private sealed class RunningScanner(IHostedService scanner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await scanner.StopAsync(CancellationToken.None);
    }

    public sealed record AlphaStep(Guid RequestId) : StepRequest(RequestId, nameof(AlphaStep));

    public sealed record BravoStep(Guid RequestId) : StepRequest(RequestId, nameof(BravoStep));

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved from DI by the engine.")]
    private sealed class NoopHandler<T> : IStepHandler<T> where T : StepRequest
    {
        public Task<StepResult> HandleAsync(T request, CancellationToken ct)
            => Task.FromResult(new StepResult(null));
    }

    private sealed class AlphaFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new AlphaStep(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named(AlphaStrategy)
            .Step<AlphaStep, NoopHandler<AlphaStep>>()
            .From<AlphaStep>().Terminal();
    }

    private sealed class BravoFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new BravoStep(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named(BravoStrategy)
            .Step<BravoStep, NoopHandler<BravoStep>>()
            .From<BravoStep>().Terminal();
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved from DI by the engine.")]
    private sealed class NoopCallbackPort : ICallbackPort
    {
        public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
    }
}
