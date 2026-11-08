using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Features.Retry;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// Runs the retry path with all three of its real collaborators over a real
/// <see cref="SqliteWorkflowDatabase"/>: <c>RequestWorker</c> consuming the real
/// <c>ChannelQueueAdapter</c>, <see cref="RetryScheduledScanner"/> putting due rows back on it, and
/// <see cref="WorkflowDbContextRepository"/> underneath both.
///
/// <para>
/// No <c>IRequestRepository</c> fake, deliberately (T-121): the defect this harness exists for lives in the
/// interaction between worker, scanner and repository — the worker appends a step row, the scanner keeps
/// selecting the row that row superseded — and a repository fake makes all three agree with each other. That
/// is how the defect behind ADR-0067 survived its tests.
/// </para>
///
/// <para>
/// <see cref="RetryScannerHarness"/> is the scanner-only sibling: it runs no worker and its flows are no-op
/// handlers, so it cannot host a handler that throws. This one runs the worker, so the handler is where the
/// interesting behaviour lives — see <see cref="AlwaysRetryableHandler"/>, which throws
/// <see cref="RetryableException"/> on every invocation and counts them per request.
/// </para>
/// </summary>
internal sealed class RetryBudgetHarness : IAsyncDisposable
{
    /// <summary>Worker and scanner share one id, as they do in one process — one lease owner.</summary>
    public const string WorkerId = "retry-budget-worker";

    /// <summary>How often every <c>WaitFor…</c> here re-checks its condition.</summary>
    public static readonly TimeSpan PollGranularity = TimeSpan.FromMilliseconds(25);

    private readonly SqliteWorkflowDatabase _db;
    private readonly ServiceProvider _provider;
    private readonly List<IHostedService> _running;

    private RetryBudgetHarness(
        SqliteWorkflowDatabase db,
        ServiceProvider provider,
        List<IHostedService> running)
    {
        _db = db;
        _provider = provider;
        _running = running;
    }

    /// <summary>
    /// Builds the container, starts <c>RequestWorker</c> and — unless <paramref name="withScanner"/> is
    /// <c>false</c> — <see cref="RetryScheduledScanner"/>, and hands back a running engine.
    /// <c>RequestWorkerStartupRecovery</c> is left out: it is a separate concern with its own tests, and it
    /// would re-enqueue rows these tests are counting.
    /// </summary>
    /// <param name="maxStepRetries">The budget under test.</param>
    /// <param name="retryBackoffBase">First-retry back-off; retry <c>n</c> is meant to wait <c>base * 2^(n-1)</c>.</param>
    /// <param name="retryPollingInterval">Scanner poll interval.</param>
    /// <param name="queueCapacity">Channel capacity — small values put the drop-on-full path (ADR-0100) under pressure.</param>
    /// <param name="withScanner">
    /// <c>false</c> leaves redelivery to the test, which is how a test measures one specific attempt without
    /// racing a scanner that may deliver the same row twice.
    /// </param>
    /// <param name="ct">Cancellation for the fixture setup.</param>
    public static async Task<RetryBudgetHarness> CreateAsync(
        int maxStepRetries = 1,
        TimeSpan? retryBackoffBase = null,
        TimeSpan? retryPollingInterval = null,
        int queueCapacity = 1000,
        bool withScanner = true,
        CancellationToken ct = default)
    {
        var db = await SqliteWorkflowDatabase.CreateAsync(ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db.Factory);
        services.AddScoped<IRequestRepository>(_ => db.CreateRepository());
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<PoisonFlow>();
        services.AddFlow<HealthyFlow>();

        // why: WorkflowEngineOptions members are init-only, so the whole IOptions instance is replaced.
        services.AddSingleton<IOptions<WorkflowEngineOptions>>(Options.Create(new WorkflowEngineOptions
        {
            WorkerId = WorkerId,
            LeaseDuration = TimeSpan.FromMinutes(5),
            MaxStepRetries = maxStepRetries,
            RetryBackoffBase = retryBackoffBase ?? TimeSpan.FromMilliseconds(250),
            RetryPollingInterval = retryPollingInterval ?? TimeSpan.FromMilliseconds(50),
            QueueCapacity = queueCapacity,
        }));

#pragma warning disable IDISP001, IDISP004 // why: ownership passes to the harness, which disposes it
        var provider = services.BuildServiceProvider();
#pragma warning restore IDISP001, IDISP004

        var running = provider.GetServices<IHostedService>()
            .Where(service => service is RequestWorker || (withScanner && service is RetryScheduledScanner))
            .ToList();

        foreach (var service in running)
        {
            await service.StartAsync(CancellationToken.None);
        }

        return new RetryBudgetHarness(db, provider, running);
    }

    /// <summary>Submits a request whose only step always throws <see cref="RetryableException"/>.</summary>
    public Task<Guid> SubmitPoisonAsync() => SubmitAsync<PoisonFlow>();

    /// <summary>Submits an unrelated request whose step succeeds immediately.</summary>
    public Task<Guid> SubmitHealthyAsync() => SubmitAsync<HealthyFlow>();

    private async Task<Guid> SubmitAsync<TFlow>() where TFlow : class, IFlowDefinition, new()
    {
        using var scope = _provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
        return await client.StartAsync<TFlow>(new object(), ct: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Puts the poison step back on the queue by hand — what the scanner would do once the retry falls due.
    /// Tests that measure one specific attempt use this instead of waiting for the scanner, so the number of
    /// deliveries is the test's decision and not a race with the poll interval.
    /// </summary>
    public async Task RedeliverPoisonStepAsync(Guid requestId)
    {
        var queue = _provider.GetRequiredService<IQueuePort>();
        await queue.EnqueueAsync(new PoisonStep(requestId), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs <c>RequestWorkerStartupRecovery</c> once, against this harness's own repository and queue — the
    /// process coming back up after a worker died mid-attempt. <see cref="CreateAsync"/> deliberately leaves
    /// it out of the hosted services it starts, so a test that wants recovery asks for it here and decides
    /// when it runs.
    /// </summary>
    public async Task RunStartupRecoveryAsync()
    {
        var recovery = _provider.GetServices<IHostedService>()
            .OfType<RequestWorkerStartupRecovery>()
            .Single();

        await recovery.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Leaves the step's live row exactly as a worker killed mid-attempt leaves it: <c>Running</c>, holding
    /// the input it was started from, under a lease that has since expired — the state
    /// <c>RecordStepStarted</c> persists before the handler is invoked, minus the process that was going to
    /// finish it. Written straight to the row rather than through the aggregate, because no domain method
    /// produces a crash.
    /// <para>
    /// Returns <c>false</c> when the step has no live row left to stall, which is how a crash-loop test
    /// stops once the step has reached a terminal status.
    /// </para>
    /// </summary>
    public async Task<bool> StallLiveStepAsync(Guid requestId, string stepName)
    {
        await using var ctx = await _db.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var rows = await ctx.RequestSteps
            .Where(s => s.RequestId == requestId && s.StepName == stepName)
            .ToListAsync(TestContext.Current.CancellationToken);

        var live = rows.FindLast(s => s.ExecutionStatus is StepExecutionStatus.Running
                                                        or StepExecutionStatus.RetryScheduled);

        if (live is null)
        {
            return false;
        }

        live.ExecutionStatus = StepExecutionStatus.Running;
        live.NextRetryAt = null;
        live.ErrorCode = null;
        live.ErrorMessage = null;
        live.LockedBy = WorkerId;
        live.LockedUntil = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return true;
    }

    /// <summary>
    /// Runs an operator's retry through the real <see cref="RetryHandler"/> — the manual path behind
    /// <c>RequestApplicationService.RetryLastStepAsync</c>, which resets the step and re-enqueues it itself.
    /// It is resolved from this harness's own container, so it shares the repository and the queue with the
    /// running worker and the retried delivery lands on the same engine the automatic path uses.
    /// </summary>
    public async Task RetryLastStepThroughRetryHandlerAsync(Guid requestId)
    {
        using var scope = _provider.CreateScope();
        var retry = scope.ServiceProvider.GetRequiredService<RetryHandler>();
        await retry.RetryLastStepAsync(requestId, TestContext.Current.CancellationToken);
    }

    /// <summary>How many times the poison handler has run for this request.</summary>
    public static int PoisonInvocations(Guid requestId) => AlwaysRetryableHandler.InvocationsFor(requestId);

    /// <summary>Reads the persisted aggregate, tolerating the fixture's SQLite write locks.</summary>
    public Task<RequestAggregate?> LoadAsync(Guid requestId) => ReadThroughWriteLocksAsync(
        repository => repository.FindByIdAsync(requestId, TestContext.Current.CancellationToken));

    /// <summary>
    /// The <c>(RequestId, StepName)</c> of every row <c>FindDueRetriesAsync</c> would hand the scanner at
    /// <paramref name="now"/> — the selection itself rather than a count, so a test can group it.
    /// </summary>
    public Task<IReadOnlyList<(Guid RequestId, string StepName)>> DueRetriesAsync(DateTimeOffset now)
        => ReadThroughWriteLocksAsync(async repository =>
        {
            var due = await repository.FindDueRetriesAsync(now, TestContext.Current.CancellationToken);

            return (IReadOnlyList<(Guid, string)>)due
                .Select(entry => (entry.Aggregate.RequestId, entry.Step.StepName))
                .ToList();
        });

    /// <summary>Waits until the poison handler has run <paramref name="attempts"/> times for this request.</summary>
    public static Task<bool> WaitForPoisonAttemptsAsync(Guid requestId, int attempts, TimeSpan timeout)
        => WaitForAsync(() => Task.FromResult(PoisonInvocations(requestId) >= attempts), timeout);

    /// <summary>
    /// Waits until the retries recorded for <paramref name="stepName"/>, <em>summed over every row that
    /// shares the name</em>, reach <paramref name="total"/>. Summing is what makes this usable as a
    /// synchronisation point on both sides of the fix: one row carrying <c>StepRetries = 2</c> and two rows
    /// carrying <c>1</c> each are the same number of recorded attempts, so a test can wait for an attempt to
    /// be persisted without asserting anything about how many rows hold it.
    /// </summary>
    public Task<bool> WaitForRecordedRetriesAsync(Guid requestId, string stepName, int total, TimeSpan timeout)
        => WaitForAsync(
            async () =>
            {
                var aggregate = await LoadAsync(requestId);
                return aggregate is not null
                    && aggregate.Steps.Where(s => s.StepName == stepName).Sum(s => s.StepRetries) >= total;
            },
            timeout);

    /// <summary>Waits until the request reaches <paramref name="expected"/>.</summary>
    public Task<bool> WaitForStatusAsync(Guid requestId, RequestStatus expected, TimeSpan timeout)
        => WaitForAsync(async () => (await LoadAsync(requestId))?.Status == expected, timeout);

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

            await Task.Delay(PollGranularity);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return await condition();
    }

    /// <summary>
    /// Runs <paramref name="read"/> against a fresh repository, retrying while SQLite reports the fixture's
    /// own write lock: every <c>DbContext</c> opens its own connection to the shared-cache database, so a
    /// read landing mid-write of the worker's own save is an expected race rather than a failure. Out of
    /// retries the real exception is left to fail the test, rather than a silent last value.
    /// </summary>
    private async Task<T> ReadThroughWriteLocksAsync<T>(Func<WorkflowDbContextRepository, Task<T>> read)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                return await read(_db.CreateRepository());
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                // why: 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED — the fixture's own write lock, not a defect.
                await Task.Delay(PollGranularity);
            }
        }

        return await read(_db.CreateRepository());
    }

    /// <summary>
    /// Stops the workers before disposing anything they use. On the unfixed code the retry loop is unbounded,
    /// so a worker left running outlives the test method and keeps writing to a database being disposed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var service in _running)
        {
            await service.StopAsync(CancellationToken.None);
        }

        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    internal sealed record PoisonStep(Guid RequestId) : StepRequest(RequestId, nameof(PoisonStep));

    internal sealed record HealthyStep(Guid RequestId) : StepRequest(RequestId, nameof(HealthyStep));

    /// <summary>
    /// Throws <see cref="RetryableException"/> every time — the <c>InitializeHeappe</c> step of the incident,
    /// reduced to its essential behaviour.
    ///
    /// <para>
    /// <c>HardStop</c> is the safety net, not the subject: past it the handler throws a
    /// <see cref="PermanentException"/> so that even a run whose assertions have already failed terminates
    /// instead of looping until the test host is killed. A test that reaches the hard stop has failed by
    /// definition — the budget it asserts is far below it.
    /// </para>
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved from DI by the worker.")]
    private sealed class AlwaysRetryableHandler : IStepHandler<PoisonStep>
    {
        private const int HardStop = 25;

        // why: DI resolves handlers by type, so the count lives on the type, keyed per request.
        // warn: never cleared — request ids are unique, and xunit runs test classes in parallel, so clearing at harness creation would reset a count another class is still measuring.
        private static readonly ConcurrentDictionary<Guid, int> Counts = new();

        public static int InvocationsFor(Guid requestId) => Counts.TryGetValue(requestId, out var n) ? n : 0;

        public Task<StepResult> HandleAsync(PoisonStep request, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(request);

            var attempt = Counts.AddOrUpdate(request.RequestId, 1, static (_, count) => count + 1);

            if (attempt >= HardStop)
            {
                throw new PermanentException(
                    "TEST_HARD_STOP",
                    $"The test harness stopped an unbounded retry loop at invocation {attempt}.");
            }

            throw new RetryableException(
                "TRANSIENT_TEST",
                $"invocation {attempt} of the always-retryable step failed transiently");
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved from DI by the worker.")]
    private sealed class HealthyHandler : IStepHandler<HealthyStep>
    {
        public Task<StepResult> HandleAsync(HealthyStep request, CancellationToken ct)
            => Task.FromResult(new StepResult(null));
    }

    private sealed class PoisonFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new PoisonStep(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("retry-budget-poison")
            .Step<PoisonStep, AlwaysRetryableHandler>()
            .From<PoisonStep>().Terminal();
    }

    private sealed class HealthyFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new HealthyStep(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("retry-budget-healthy")
            .Step<HealthyStep, HealthyHandler>()
            .From<HealthyStep>().Terminal();
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved from DI by the worker.")]
    private sealed class NoopCallbackPort : ICallbackPort
    {
        public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
    }
}
