using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Core.Adapters;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Workers;

/// <summary>
/// Background worker that consumes <see cref="StepRequest"/> messages from <see cref="IQueuePort"/>
/// and drives the typed-state workflow execution loop.
///
/// Per message:
/// 1. Load <see cref="Domain.Request.RequestAggregate"/> from <see cref="IRequestRepository"/>.
/// 2. Acquire step lease (skip if another instance holds a live lease).
/// 3. Persist <c>Running</c> state + input data before invoking the handler.
/// 4. Invoke <c>IStepHandler&lt;T&gt;</c> resolved from DI by message type.
/// 5. On success: persist <c>Completed</c>, call <c>strategy.Transition</c>, enqueue next or close flow.
/// 6. On <see cref="RetryableException"/>: persist <c>RetryScheduled</c> with back-off.
/// 7. On permanent failure: persist <c>Failed</c> for step and flow.
/// </summary>
internal sealed class RequestWorker : BackgroundService
{
    private readonly IQueuePort _queue;
    private readonly IStrategyRegistry _registry;
    private readonly IServiceScopeFactory _services;
    private readonly ICallbackPort _callbackPort;
    private readonly WorkflowEngineOptions _options;
    private readonly ILogger<RequestWorker> _logger;

    public RequestWorker(
        IQueuePort queue,
        IStrategyRegistry registry,
        IServiceScopeFactory services,
        ICallbackPort callbackPort,
        IOptions<WorkflowEngineOptions> options,
        ILogger<RequestWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(callbackPort);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _queue = queue;
        _registry = registry;
        _services = services;
        _callbackPort = callbackPort;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("RequestWorker started (WorkerId={WorkerId})", _options.WorkerId);

        await foreach (var message in _queue.ConsumeAsync(ct))
        {
            using var scope = _services.CreateScope();
            await DispatchAsync(scope.ServiceProvider, message, ct);
        }
    }

    private async Task DispatchAsync(IServiceProvider sp, StepRequest message, CancellationToken ct)
    {
        var repo = sp.GetRequiredService<IRequestRepository>();

        var aggregate = await repo.FindByIdAsync(message.RequestId, ct);
        if (aggregate is null)
        {
            _logger.LogWarning("RequestWorker: aggregate {RequestId} not found for step '{StateName}'; skipping",
                message.RequestId, message.StateName);
            return;
        }

        var strategy = _registry.Resolve(aggregate.Strategy);

        // Crash-recovery guard: step already completed — skip re-execution and advance
        if (aggregate.IsStepCompleted(message.StateName))
        {
            _logger.LogDebug("Step '{StateName}' on {RequestId} already completed; replaying transition",
                message.StateName, message.RequestId);

            var existingNext = strategy.Transition(
                message,
                new StepResult(aggregate.GetStepResult(message.StateName)),
                aggregate);

            // A GateStep means the request is already parked (persisted in the same save as the
            // completed step); do not re-enqueue it on replay.
            if (existingNext is not null and not GateStep)
            {
                await _queue.EnqueueAsync(existingNext, ct);
            }

            return;
        }

        // Multi-instance guard: try to acquire an exclusive step lease
        var leaseAcquired = await repo.TryAcquireStepLeaseAsync(
            aggregate.RequestId, message.StateName,
            _options.WorkerId, _options.LeaseDuration, ct);

        if (!leaseAcquired)
        {
            _logger.LogDebug("Step '{StateName}' on {RequestId} is already leased; skipping",
                message.StateName, message.RequestId);
            return;
        }

        // Persist Running + input before invoking handler — enables deterministic replay
        var now = DateTimeOffset.UtcNow;
        var inputData = JsonSerializer.SerializeToDocument(message, message.GetType());
        aggregate.RecordStepStarted(message.StateName, inputData, message.GetType().AssemblyQualifiedName!, now);

        try
        {
            await repo.SaveAsync(aggregate, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // On a step's first execution there is no row to lease, so two workers can both get here.
            // The aggregate's rowversion decides: the loser drops the step, exactly as a lost lease would.
            _logger.LogDebug("Step '{StateName}' on {RequestId} was claimed by another worker; skipping",
                message.StateName, message.RequestId);
            return;
        }

        try
        {
            _logger.LogDebug(
                "Invoking handler for step '{StateName}' on request {RequestId}",
                message.StateName, message.RequestId);

            var invoker = sp.GetRequiredKeyedService<IStepHandlerInvoker>(message.GetType());
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await invoker.InvokeAsync(message, ct);
            stopwatch.Stop();

            _logger.LogDebug(
                "Handler for step '{StateName}' completed in {ElapsedMs}ms",
                message.StateName, stopwatch.ElapsedMilliseconds);

            now = DateTimeOffset.UtcNow;

            // Pure transition — no I/O; called before any persistence
            var next = strategy.Transition(message, result, aggregate);

            aggregate.RecordStepCompleted(message.StateName, result.Data, now);

            switch (next)
            {
                case null:
                aggregate.MarkCompleted(result.Data, now);
                break;
                case GateStep gate:
                aggregate.MarkAwaitingSignal(gate.GateName, now);
                break;
                default:
                aggregate.SetCurrentState(next.StateName, now);
                break;
            }

            await repo.SaveAsync(aggregate, ct);
            await repo.ReleaseStepLeaseAsync(aggregate.RequestId, message.StateName, ct);

            switch (next)
            {
                case GateStep gate:
                _logger.LogInformation(
                    "Request {RequestId} parked at gate '{Gate}' awaiting a signal",
                    message.RequestId, gate.GateName);
                break;

                case not null:
                _logger.LogDebug(
                    "Enqueuing next step '{NextStep}' for request {RequestId}",
                    next.StateName, message.RequestId);
                await _queue.EnqueueAsync(next, ct);
                break;

                default:
                if (aggregate.CallbackUrl is not null)
                {
                    _logger.LogDebug(
                        "Flow for request {RequestId} completed, sending callback to {CallbackUrl}",
                        aggregate.RequestId, aggregate.CallbackUrl);
                    await _callbackPort.SendAsync(aggregate, ct);
                }

                break;
            }

            _logger.LogInformation(
                "Step '{StateName}' on {RequestId} completed successfully",
                message.StateName, message.RequestId);
        }
        catch (RetryableException ex) when (!IsRetryBudgetExhausted(aggregate, message.StateName))
        {
            now = DateTimeOffset.UtcNow;
            var retries = (aggregate.FindLiveStep(message.StateName)?.StepRetries ?? 0) + 1;
            var delay = ComputeBackoff(retries);
            var nextRetryAt = now.Add(delay);

            _logger.LogWarning(ex,
                "Step '{StateName}' on {RequestId} failed (retryable); retry #{Retry} at {NextRetryAt}",
                message.StateName, message.RequestId, retries, nextRetryAt);

            aggregate.RecordStepRetryScheduled(message.StateName, ex.ErrorInfo, nextRetryAt, now);
            await repo.SaveAsync(aggregate, ct);
            await repo.ReleaseStepLeaseAsync(aggregate.RequestId, message.StateName, ct);
        }
        catch (Exception ex)
        {
            now = DateTimeOffset.UtcNow;
            var error = ex is PermanentException pe
                ? pe.ErrorInfo
                : new Contracts.Exceptions.ErrorInfo("PERMANENT_FAILURE", ex.Message);

            _logger.LogError(ex,
                "Step '{StateName}' on {RequestId} failed permanently",
                message.StateName, message.RequestId);

            aggregate.RecordStepFailed(message.StateName, error, now);
            aggregate.MarkFailed(error, now);
            await repo.SaveAsync(aggregate, ct);
            await repo.ReleaseStepLeaseAsync(aggregate.RequestId, message.StateName, ct);
        }
    }

    private bool IsRetryBudgetExhausted(Domain.Request.RequestAggregate aggregate, string stateName)
    {
        var step = aggregate.FindLiveStep(stateName);
        return step is not null && step.StepRetries >= _options.MaxStepRetries;
    }

    private TimeSpan ComputeBackoff(int retryNumber)
    {
        // Exponential back-off: base * 2^(retry-1), capped at 10 minutes
        var factor = Math.Pow(2, retryNumber - 1);
        var backoff = TimeSpan.FromTicks((long)(_options.RetryBackoffBase.Ticks * factor));
        return backoff > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : backoff;
    }
}