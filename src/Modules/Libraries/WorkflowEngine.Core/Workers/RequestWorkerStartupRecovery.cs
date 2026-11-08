using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Workers;

/// <summary>
/// Runs once at application startup and recovers requests the in-memory queue cannot be trusted to have
/// kept — the durability boundary is the <c>workflow.Requests</c> row, not the channel (ADR-0100). Three
/// classes are recovered:
/// <list type="bullet">
///   <item>A <c>Running</c> step whose lease expired — the worker crashed mid-step. Reset via
///   <see cref="Domain.Request.RequestAggregate.ResetStepForRecovery"/> — which keeps the retry budget the
///   crashed attempt already spent — and re-enqueued from persisted
///   <c>InputData</c>.</item>
///   <item>An <c>Accepted</c> request with no step row — submitted, but the initial enqueue never reached a
///   worker. Re-enqueued by reconstructing the initial step from the aggregate's <c>Payload</c>.</item>
///   <item>An <c>InProgress</c> request with no <c>Running</c> step — the flow advanced past its last
///   completed step but the next enqueue was lost. Re-enqueued by replaying the strategy's pure
///   <c>Transition</c> over the last completed step to reconstruct the actual next message.</item>
/// </list>
/// None of the last two are lease-gated, so this must stay a one-shot <see cref="IHostedService"/> registered
/// before <see cref="RequestWorker"/> (ADR-0067) — a periodic scan would duplicate live in-flight steps, since
/// "no Running step yet" is also the normal gap between a step completing and the next one starting.
///
/// <see cref="IRequestRepository"/> is scoped, so a scope is created for this one-shot run rather
/// than injecting it into this singleton hosted service's constructor.
/// </summary>
internal sealed class RequestWorkerStartupRecovery : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IQueuePort _queue;
    private readonly IStrategyRegistry _registry;
    private readonly WorkflowEngineOptions _options;
    private readonly ILogger<RequestWorkerStartupRecovery> _logger;

    public RequestWorkerStartupRecovery(
        IServiceScopeFactory scopeFactory,
        IQueuePort queue,
        IStrategyRegistry registry,
        IOptions<WorkflowEngineOptions> options,
        ILogger<RequestWorkerStartupRecovery> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _queue = queue;
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRequestRepository>();

        var leaseExpiredBefore = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "RequestWorkerStartupRecovery: scanning for stalled steps (leaseExpiredBefore={Cutoff})",
            leaseExpiredBefore);

        var stalled = await repository.FindStalledAsync(leaseExpiredBefore, ct);
        var recovered = 0;

        foreach (var aggregate in stalled)
        {
            var stalledSteps = aggregate.Steps
                .Where(s => s.ExecutionStatus == StepExecutionStatus.Running
                         && s.LockedUntil.HasValue
                         && s.LockedUntil.Value < leaseExpiredBefore)
                .ToList();

            foreach (var step in stalledSteps)
            {
                var type = Type.GetType(step.MessageType);
                if (type is null)
                {
                    _logger.LogError(
                        "StartupRecovery: cannot resolve type '{MessageType}' for step '{StepName}' on {RequestId}",
                        step.MessageType, step.StepName, aggregate.RequestId);
                    continue;
                }

                StepRequest? message = null;
                try
                {
                    message = step.InputData is not null
                        ? JsonSerializer.Deserialize(step.InputData.RootElement.GetRawText(), type) as StepRequest
                        : null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "StartupRecovery: failed to deserialize InputData for step '{StepName}' on {RequestId}",
                        step.StepName, aggregate.RequestId);
                    continue;
                }

                if (message is null)
                {
                    _logger.LogWarning(
                        "StartupRecovery: null message for step '{StepName}' on {RequestId}; skipping",
                        step.StepName, aggregate.RequestId);
                    continue;
                }

                aggregate.ResetStepForRecovery(step.StepName, DateTimeOffset.UtcNow);
                await repository.SaveAsync(aggregate, ct);
                await _queue.EnqueueAsync(message, ct);
                recovered++;

                _logger.LogInformation(
                    "StartupRecovery: re-enqueued stalled step '{StepName}' on {RequestId}",
                    step.StepName, aggregate.RequestId);
            }
        }

        _logger.LogInformation("RequestWorkerStartupRecovery: recovered {Count} stalled step(s)", recovered);

        var stranded = await repository.FindStrandedAsync(ct);

        foreach (var aggregate in stranded)
        {
            try
            {
                var message = aggregate.Status == RequestStatus.Accepted
                    ? ReconstructInitialStep(aggregate)
                    : ReconstructNextStep(aggregate);

                if (message is null)
                {
                    continue;
                }

                await _queue.EnqueueAsync(message, ct);
                recovered++;

                _logger.LogInformation(
                    "StartupRecovery: re-enqueued stranded step '{StateName}' on {RequestId}",
                    message.StateName, aggregate.RequestId);
            }
            catch (Exception ex)
            {
                // Backstop: whatever went wrong reconstructing or re-enqueuing this one row — a malformed
                // persisted payload throwing something other than JsonException, an EnqueueAsync failure, or
                // anything else unanticipated — must not abort recovery for every other stranded row still
                // waiting in this loop, and must not escape StartAsync and take the whole host down.
                _logger.LogWarning(ex,
                    "StartupRecovery: could not recover {RequestId}; skipping",
                    aggregate.RequestId);
            }
        }

        _logger.LogInformation(
            "RequestWorkerStartupRecovery: recovered {Count} stalled/stranded step(s) in total", recovered);
    }

    /// <summary>
    /// Reconstructs the initial step for an <c>Accepted</c> request that has no step row — its <c>Payload</c>
    /// is the serialised initial step (<c>SubmitHandler</c> stores it verbatim before the first enqueue).
    /// </summary>
    private StepRequest? ReconstructInitialStep(RequestAggregate aggregate)
    {
        IRequestStrategy strategy;
        try
        {
            strategy = _registry.Resolve(aggregate.Strategy);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "StartupRecovery: no strategy registered for '{Strategy}' on {RequestId}; skipping",
                aggregate.Strategy, aggregate.RequestId);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                aggregate.Payload.RootElement.GetRawText(), strategy.InitialStepType) as StepRequest;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "StartupRecovery: failed to deserialize Payload as '{InitialStepType}' for {RequestId}",
                strategy.InitialStepType, aggregate.RequestId);
            return null;
        }
    }

    /// <summary>
    /// Reconstructs the next step for an <c>InProgress</c> request with no <c>Running</c> step, by replaying
    /// the strategy's pure <c>Transition</c> over the last completed step — the same computation that would
    /// have produced the message the lost enqueue was carrying.
    /// <para>
    /// This does not cover a genuine gate resume: the resumed step there comes from <c>gate.Resume</c>, not
    /// from <c>Transition</c>, and neither the signal nor the resumed step is persisted. Replaying the last
    /// completed step in that case routes back into the <c>GateStep</c> the request already left, which is
    /// filtered out below rather than re-parking the request (ADR-0100 records this as a known, open gap).
    /// </para>
    /// </summary>
    private StepRequest? ReconstructNextStep(RequestAggregate aggregate)
    {
        var lastCompleted = aggregate.Steps
            .Where(s => s.ExecutionStatus == StepExecutionStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .FirstOrDefault();

        if (lastCompleted is null)
        {
            _logger.LogWarning(
                "StartupRecovery: {RequestId} is InProgress with no completed step to replay from; skipping",
                aggregate.RequestId);
            return null;
        }

        var type = Type.GetType(lastCompleted.MessageType);
        if (type is null)
        {
            _logger.LogError(
                "StartupRecovery: cannot resolve type '{MessageType}' for step '{StepName}' on {RequestId}",
                lastCompleted.MessageType, lastCompleted.StepName, aggregate.RequestId);
            return null;
        }

        StepRequest? completedMessage;
        try
        {
            completedMessage = lastCompleted.InputData is not null
                ? JsonSerializer.Deserialize(lastCompleted.InputData.RootElement.GetRawText(), type) as StepRequest
                : null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "StartupRecovery: failed to deserialize InputData for step '{StepName}' on {RequestId}",
                lastCompleted.StepName, aggregate.RequestId);
            return null;
        }

        if (completedMessage is null)
        {
            _logger.LogWarning(
                "StartupRecovery: null message for completed step '{StepName}' on {RequestId}; skipping",
                lastCompleted.StepName, aggregate.RequestId);
            return null;
        }

        IRequestStrategy strategy;
        try
        {
            strategy = _registry.Resolve(aggregate.Strategy);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "StartupRecovery: no strategy registered for '{Strategy}' on {RequestId}; skipping",
                aggregate.Strategy, aggregate.RequestId);
            return null;
        }

        var next = strategy.Transition(
            completedMessage, new StepResult(lastCompleted.Result), aggregate);

        // A GateStep means replay routed back into the gate the request already left behind — the
        // unrecoverable gate-resume case (ADR-0100). Anything that doesn't land on the persisted
        // CurrentState is stale and must not be enqueued.
        if (next is null or GateStep || next.StateName != aggregate.CurrentState)
        {
            _logger.LogWarning(
                "StartupRecovery: cannot recover {RequestId} at '{CurrentState}' from its last completed step — replay produced {Next}; this is the known gate-resume gap (ADR-0100)",
                aggregate.RequestId, aggregate.CurrentState, next?.GetType().Name ?? "null");
            return null;
        }

        return next;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}