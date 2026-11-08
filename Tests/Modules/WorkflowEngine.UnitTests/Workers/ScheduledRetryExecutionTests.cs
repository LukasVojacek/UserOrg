using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// A step that failed retryably is parked as <c>RetryScheduled</c> and picked up later by
/// <see cref="RetryScheduledScanner"/>, which leases it and re-enqueues its message.
/// <see cref="RequestWorker"/> then has to actually run it.
///
/// Both run in the same process and share one <c>WorkerId</c>, so the scanner's lease and the worker's
/// claim are the same owner. The repository is the real one on a relational provider: EF InMemory cannot
/// run the <c>ExecuteUpdateAsync</c> behind the lease and the in-memory fake grants every lease, so
/// neither can tell whether the retry is executed or dropped.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ScheduledRetryExecutionTests : IAsyncLifetime
{
    private const string StrategyName = "scheduled-retry";
    private const string WorkerId = "worker-under-test";

    private SqliteWorkflowDatabase _db = null!;

    public async ValueTask InitializeAsync()
    {
        RetryHandler.Reset();
        _db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact(DisplayName = "A due scheduled retry is executed, not dropped on the scanner's own lease")]
    public async Task Should_ExecuteTheStep_When_AScheduledRetryIsDue()
    {
        var requestId = await SeedDueRetryAsync();

        await using var provider = BuildProvider(queue: null);
        await using var host = await StartWorkersAsync(provider);

        var executed = await WaitForHandlerAsync();

        Assert.True(
            executed,
            "The due retry never ran. RetryScheduledScanner leased the step and enqueued it without releasing the lease, so RequestWorker.TryAcquireStepLeaseAsync saw a live lease — its own — and skipped the step.");

        Assert.Equal(1, RetryHandler.Invocations);

        var repository = _db.CreateRepository();

        // The handler running and the aggregate being persisted as completed are two separate, racing
        // events: RequestWorker still has to save the step's outcome after RetryHandler returns. Poll on
        // the persisted state instead of asserting immediately after the handler invocation is observed.
        var (completed, aggregate) = await WaitForStepCompletionAsync(repository, requestId);

        Assert.NotNull(aggregate);
        Assert.True(
            completed,
            $"The retried step should be recorded as completed; request status is {aggregate.Status}.");
    }

    [Fact(DisplayName = "The scanner keeps its lease when it re-enqueues a due retry")]
    public async Task Should_KeepTheLease_When_TheScannerEnqueuesADueRetry()
    {
        // The lease is also the scanner's de-dup — a due step keeps coming back from FindDueRetriesAsync
        // until a worker flips its status. Fixing the retry path by dropping this lease would turn a
        // stalled worker into a queue-filler, so this pins the scanner's half of the contract.
        var requestId = await SeedDueRetryAsync();
        var queue = new RecordingQueue();

        await using var provider = BuildProvider(queue);
        await using var host = await StartWorkersAsync(provider, includeWorker: false);

        var enqueued = await WaitForAsync(() => queue.Messages.Count > 0);
        Assert.True(enqueued, "RetryScheduledScanner did not re-enqueue the due step.");

        var message = Assert.IsType<RetryStep>(queue.Messages.First());
        Assert.Equal(requestId, message.RequestId);

        var repository = _db.CreateRepository();
        var aggregate = await repository.FindByIdAsync(requestId, TestContext.Current.CancellationToken);
        Assert.NotNull(aggregate);

        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(WorkerId, step.LockedBy);
        Assert.NotNull(step.LockedUntil);
        Assert.True(step.LockedUntil > DateTimeOffset.UtcNow, "The scanner's lease must still be live.");
    }

    private async Task<Guid> SeedDueRetryAsync()
    {
        var requestId = Guid.CreateVersion7();
        var message = new RetryStep(requestId);

        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: nameof(RetryStep),
            executionStatus: StepExecutionStatus.RetryScheduled,
            messageType: typeof(RetryStep).AssemblyQualifiedName!,
            inputData: JsonSerializer.SerializeToDocument(message, message.GetType()),
            stepRetries: 1,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            errorCode: "TRANSIENT",
            errorMessage: "HEAppE timed out");

        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            strategy: StrategyName,
            status: RequestStatus.InProgress,
            currentState: nameof(RetryStep),
            steps: [step]);

        await _db.CreateRepository().AddAsync(aggregate, TestContext.Current.CancellationToken);
        return requestId;
    }

    private ServiceProvider BuildProvider(IQueuePort? queue)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddScoped<IRequestRepository>(_ => _db.CreateRepository());
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<RetryFlow>();

        if (queue is not null)
        {
            services.AddSingleton(queue);
        }

        // The options are init-only, so the whole IOptions instance is replaced rather than configured.
        // One scan is all the test needs; a short interval would let the scanner re-enqueue a step the
        // worker has not finished with yet and make the invocation count racy.
        services.AddSingleton<IOptions<WorkflowEngineOptions>>(Options.Create(new WorkflowEngineOptions
        {
            WorkerId = WorkerId,
            LeaseDuration = TimeSpan.FromMinutes(5),
            RetryPollingInterval = TimeSpan.FromSeconds(30),
        }));

        return services.BuildServiceProvider();
    }

    private static async Task<RunningWorkers> StartWorkersAsync(
        IServiceProvider provider,
        bool includeWorker = true)
    {
        // RequestWorkerStartupRecovery is deliberately left out: this test is about the scheduled-retry
        // path, and stalled-step recovery is a separate concern with its own tests.
        var started = provider.GetServices<IHostedService>()
            .Where(service => service is RetryScheduledScanner || (includeWorker && service is RequestWorker))
            .ToList();

        foreach (var service in started)
        {
            await service.StartAsync(CancellationToken.None);
        }

        return new RunningWorkers(started);
    }

    private static Task<bool> WaitForHandlerAsync() => WaitForAsync(() => RetryHandler.Invocations > 0);

    // The workers run on their own threads; poll instead of guessing a delay that holds under load.
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        return condition();
    }

    // RetryHandler.Invocations observes the handler running; it says nothing about whether RequestWorker
    // has since persisted the step as completed. Poll the repository itself so the assertion synchronises
    // on the same state it checks, rather than racing the save under full-suite CPU load.
    private static async Task<(bool Completed, RequestAggregate? Aggregate)> WaitForStepCompletionAsync(
        IRequestRepository repository, Guid requestId)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        RequestAggregate? aggregate = null;
        do
        {
            try
            {
                aggregate = await repository.FindByIdAsync(requestId, ct);
                if (aggregate is not null && aggregate.IsStepCompleted(nameof(RetryStep)))
                {
                    return (true, aggregate);
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                // 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED: each DbContext holds its own connection to the
                // fixture's shared-cache database, so a poll landing mid-write on RequestWorker's own
                // transaction hits a SQLite-level write lock — an expected race, not a failure. Treat it
                // as "not observed yet" and poll again.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return (false, aggregate);
    }

    private sealed class RunningWorkers(IReadOnlyList<IHostedService> services) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var service in services)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    internal sealed record RetryStep(Guid RequestId) : StepRequest(RequestId, nameof(RetryStep));

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved from DI by the worker.")]
    private sealed class RetryHandler : IStepHandler<RetryStep>
    {
        // The engine resolves handlers by type from DI, so the count lives on the type.
        private static int _invocations;

        public static int Invocations => Volatile.Read(ref _invocations);

        public static void Reset() => Volatile.Write(ref _invocations, 0);

        public Task<StepResult> HandleAsync(RetryStep request, CancellationToken ct)
        {
            Interlocked.Increment(ref _invocations);
            return Task.FromResult(new StepResult(null));
        }
    }

    private sealed class RetryFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new RetryStep(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named(StrategyName)
            .Step<RetryStep, RetryHandler>()
            .From<RetryStep>().Terminal();
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
