using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Workers;

/// <summary>
/// Periodic background scanner that finds steps in <c>RetryScheduled</c> status whose
/// <c>NextRetryAt</c> has elapsed and re-enqueues their messages into <see cref="IQueuePort"/>.
///
/// The scanner uses the step lease to prevent multiple instances from re-enqueuing the same step.
/// <see cref="IRequestRepository"/> is scoped, so a fresh scope is created per polling iteration
/// rather than injecting it into this singleton hosted service's constructor.
/// </summary>
internal sealed class RetryScheduledScanner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IQueuePort _queue;
    private readonly WorkflowEngineOptions _options;
    private readonly ILogger<RetryScheduledScanner> _logger;

    /// <summary>
    /// Consecutive materialisation-failure count per (RequestId, StepName), scoped to this scanner
    /// instance's lifetime. A row that resolves cleanly is removed; a row that never resolves crosses
    /// <see cref="WorkflowEngineOptions.MaxMaterializationFailures"/> and is failed outright, so the
    /// dictionary never grows past the number of steps currently mid-budget.
    /// </summary>
    private readonly ConcurrentDictionary<(Guid RequestId, string StepName), int> _materializationFailures = new();

    public RetryScheduledScanner(
        IServiceScopeFactory scopeFactory,
        IQueuePort queue,
        IOptions<WorkflowEngineOptions> options,
        ILogger<RetryScheduledScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("RetryScheduledScanner started (poll={Interval})", _options.RetryPollingInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetryScheduledScanner encountered an error during scan");
            }

            await Task.Delay(_options.RetryPollingInterval, ct);
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRequestRepository>();

        var now = DateTimeOffset.UtcNow;
        var due = await repository.FindDueRetriesAsync(now, ct);

        foreach (var (aggregate, step) in due)
        {
            var leaseAcquired = await repository.TryAcquireStepLeaseAsync(
                aggregate.RequestId, step.StepName,
                _options.WorkerId, _options.LeaseDuration, ct);

            if (!leaseAcquired)
            {
                continue;
            }

            var type = Type.GetType(step.MessageType);
            if (type is null)
            {
                await HandleMaterializationFailureAsync(
                    repository, aggregate, step,
                    "UNRESOLVABLE_MESSAGE_TYPE",
                    $"cannot resolve type '{step.MessageType}'",
                    exception: null, ct);
                continue;
            }

            StepRequest? message = null;
            Exception? deserializationError = null;
            try
            {
                message = step.InputData is not null
                    ? JsonSerializer.Deserialize(step.InputData.RootElement.GetRawText(), type) as StepRequest
                    : null;
            }
            catch (Exception ex)
            {
                // Expected, in the sense this boundary exists for: the stored payload does not match the
                // type it claims to be (a renamed/retyped DTO member, a malformed value). Whatever the
                // concrete exception shape, it is caught here and routed through the same bounded-failure
                // path as the other two materialisation causes rather than crashing the scan loop.
                deserializationError = ex;
            }

            if (deserializationError is not null)
            {
                await HandleMaterializationFailureAsync(
                    repository, aggregate, step,
                    "DESERIALIZATION_FAILED",
                    "failed to deserialize InputData",
                    deserializationError, ct);
                continue;
            }

            if (message is null)
            {
                await HandleMaterializationFailureAsync(
                    repository, aggregate, step,
                    "NULL_MESSAGE",
                    "deserialized InputData to a null message",
                    exception: null, ct);
                continue;
            }

            _materializationFailures.TryRemove((aggregate.RequestId, step.StepName), out _);

            await _queue.EnqueueAsync(message, ct);

            _logger.LogDebug(
                "RetryScheduledScanner: re-enqueued step '{StepName}' on {RequestId} (retry #{Retries})",
                step.StepName, aggregate.RequestId, step.StepRetries);
        }
    }

    /// <summary>
    /// The one budget covering every way <see cref="ScanAsync"/> can fail to turn a due step back into a
    /// message: an unresolvable <see cref="RequestStep.MessageType"/>, a deserialization exception, or a
    /// payload that deserializes to <c>null</c>. Below the budget the lease is released so the row gets
    /// another poll (the failure may be transient — a rolling deploy mid-flight); at the budget the step is
    /// failed outright, so the row stops being selected instead of flooding the log forever.
    /// </summary>
    private async Task HandleMaterializationFailureAsync(
        IRequestRepository repository,
        RequestAggregate aggregate,
        RequestStep stepSnapshot,
        string errorCode,
        string reason,
        Exception? exception,
        CancellationToken ct)
    {
        var stepName = stepSnapshot.StepName;
        var key = (aggregate.RequestId, stepName);
        var attempt = _materializationFailures.AddOrUpdate(key, 1, static (_, count) => count + 1);

        if (attempt < _options.MaxMaterializationFailures)
        {
            // A measurable precursor with a stated consequence (the step fails once the budget is spent) —
            // Warning, not Error: nobody needs to act on any single one of these.
            _logger.LogWarning(exception,
                "RetryScheduledScanner: could not materialise message for step '{StepName}' on {RequestId} ({Reason}); attempt {Attempt}/{MaxAttempts}, releasing lease",
                stepName, aggregate.RequestId, reason, attempt, _options.MaxMaterializationFailures);

            await repository.ReleaseStepLeaseAsync(aggregate.RequestId, stepName, ct);
            return;
        }

        // The budget is spent: this is now unexpected in the sense that it will not resolve itself by
        // polling again, and an operator needs to know — Error.
        _logger.LogError(exception,
            "RetryScheduledScanner: giving up on step '{StepName}' on {RequestId} after {Attempts} consecutive materialisation failures ({Reason}); failing the step",
            stepName, aggregate.RequestId, attempt, reason);

        var now = DateTimeOffset.UtcNow;
        var error = new ErrorInfo(
            errorCode,
            $"RetryScheduledScanner could not materialise the message for step '{stepName}' after {attempt} consecutive attempts: {reason}.");

        // why: a step name that completed and then ran again has a second row sharing it, so the by-name overload could resolve to the other row and leave this one due forever.
        aggregate.RecordStepFailed(stepSnapshot, error, now);

        // Only close the request when the row we just failed is the one the flow is actually sitting on. A
        // stale row belonging to a step the flow has already moved past must not fail a request that is still
        // making progress on a later one.
        if (string.Equals(aggregate.CurrentState, stepName, StringComparison.Ordinal))
        {
            aggregate.MarkFailed(error, now);
        }

        await repository.SaveAsync(aggregate, ct);

        // After the save, so a save that throws leaves the budget spent rather than restarting it.
        _materializationFailures.TryRemove(key, out _);
    }
}