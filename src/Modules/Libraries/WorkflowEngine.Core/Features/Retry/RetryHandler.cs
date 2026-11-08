using System.Text.Json;

using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Ports;

using static System.Net.WebRequestMethods;

namespace WorkflowEngine.Core.Features.Retry;

/// <summary>
/// Re-enqueues a failed or retry-scheduled step — either the last step or a specific named step.
/// Reconstructs the original <see cref="StepRequest"/> from persisted <c>InputData</c> + <c>MessageType</c>.
/// </summary>
internal sealed class RetryHandler
{
    private readonly IRequestRepository _repository;
    private readonly IQueuePort _queue;
    private readonly ILogger<RetryHandler> _logger;

    public RetryHandler(IRequestRepository repository, IQueuePort queue, ILogger<RetryHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _queue = queue;
        _logger = logger;
    }

    public async Task RetryLastStepAsync(Guid requestId, CancellationToken ct)
    {
        _logger.LogDebug("RetryHandler: Retrying last step for request {RequestId}", requestId);

        var aggregate = await LoadAndValidateAggregate(requestId, ct);

        var lastStep = aggregate.GetLastStep()
            ?? throw new InvalidOperationException($"Request '{requestId}' has no steps to retry.");

        ValidateStepIsRetryable(lastStep, requestId);

        _logger.LogDebug(
            "RetryHandler: Retrieved last step '{StepName}' from request {RequestId}, retry #{RetryCount}",
            lastStep.StepName,
            requestId,
            lastStep.StepRetries);

        var message = DeserializeStep(lastStep);
        var now = DateTimeOffset.UtcNow;

        aggregate.ResetStepForRetry(lastStep.StepName, now);
        await _repository.SaveAsync(aggregate, ct);
        await _queue.EnqueueAsync(message, ct);

        _logger.LogInformation(
            "RetryHandler: Retry enqueued for last step '{StepName}' on request {RequestId}",
            lastStep.StepName,
            requestId);
    }

    public async Task RetryStepAsync(Guid requestId, string stateName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName, nameof(stateName));

        _logger.LogDebug(
            "RetryHandler: Retrying specific step '{StepName}' for request {RequestId}",
            stateName,
            requestId);

        var aggregate = await LoadAndValidateAggregate(requestId, ct);

        var step = aggregate.Steps.FirstOrDefault(s => s.StepName == stateName)
            ?? throw new KeyNotFoundException(
                $"Step '{stateName}' not found on request '{requestId}'.");

        ValidateStepIsRetryable(step, requestId);

        _logger.LogDebug(
            "RetryHandler: Found step '{StepName}' on request {RequestId}, retry #{RetryCount}",
            stateName,
            requestId,
            step.StepRetries);

        var message = DeserializeStep(step);
        var now = DateTimeOffset.UtcNow;

        aggregate.ResetStepForRetry(stateName, now);
        await _repository.SaveAsync(aggregate, ct);
        await _queue.EnqueueAsync(message, ct);

        _logger.LogInformation(
            "RetryHandler: Retry enqueued for step '{StepName}' on request {RequestId}",
            stateName,
            requestId);
    }

    private async Task<Domain.Request.RequestAggregate> LoadAndValidateAggregate(Guid requestId, CancellationToken ct)
    {
        var aggregate = await _repository.FindByIdAsync(requestId, ct)
            ?? throw new KeyNotFoundException($"Request '{requestId}' not found.");

        if (aggregate.Status is not (RequestStatus.InProgress or RequestStatus.Failed))
        {
            throw new InvalidOperationException(
                $"Cannot retry request '{requestId}': current status is '{aggregate.Status}'. Only InProgress or Failed requests can be retried.");
        }

        return aggregate;
    }

    private static void ValidateStepIsRetryable(Domain.Request.RequestStep step, Guid requestId)
    {
        if (step.ExecutionStatus is not (StepExecutionStatus.Completed or StepExecutionStatus.Failed or StepExecutionStatus.RetryScheduled))
        {
            throw new InvalidOperationException(
                $"Cannot retry step '{step.StepName}' on request '{requestId}': execution status is '{step.ExecutionStatus}'. Only Failed or RetryScheduled steps can be retried.");
        }

        var now = DateTimeOffset.UtcNow;
        if (step.LockedUntil.HasValue && step.LockedUntil.Value > now)
        {
            throw new InvalidOperationException(
                $"Cannot retry step '{step.StepName}' on request '{requestId}': step is currently locked by worker '{step.LockedBy}' until {step.LockedUntil:O}.");
        }
    }

    private static StepRequest DeserializeStep(Domain.Request.RequestStep step)
    {
        var type = Type.GetType(step.MessageType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve message type '{step.MessageType}'. Ensure the strategy assembly is loaded.");

        var message = step.InputData is not null
            ? JsonSerializer.Deserialize(step.InputData.RootElement.GetRawText(), type) as StepRequest
            : null;

        return message
            ?? throw new InvalidOperationException(
                $"Failed to deserialize InputData for step '{step.StepName}' as type '{step.MessageType}'.");
    }
}