using System.Collections.Immutable;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.Features.Retry;
using WorkflowEngine.Core.Features.Submit;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Application;

/// <summary>
/// Implements <see cref="IRequestApplicationService"/> by combining the submit and retry handlers
/// with inline logic for trivial operations (status, list, stats, mark-failed).
/// </summary>
internal sealed class RequestApplicationService : IRequestApplicationService
{
    private readonly SubmitHandler _submit;
    private readonly RetryHandler _retry;
    private readonly IRequestRepository _repository;
    private readonly IStrategyRegistry _registry;
    private readonly IQueuePort _queue;
    private readonly ILogger<RequestApplicationService> _logger;

    public RequestApplicationService(
        SubmitHandler submit,
        RetryHandler retry,
        IRequestRepository repository,
        IStrategyRegistry registry,
        IQueuePort queue,
        ILogger<RequestApplicationService> logger)
    {
        _submit = submit;
        _retry = retry;
        _repository = repository;
        _registry = registry;
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<SubmitResponse> SubmitAsync(
        StepRequest initialStep,
        string? clientId = null,
        string? callbackUrl = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
        => _submit.HandleAsync(initialStep, clientId, callbackUrl, idempotencyKey, ct);

    /// <inheritdoc/>
    public async Task<StatusResponse?> GetStatusAsync(Guid requestId, CancellationToken ct = default)
    {
        _logger.LogDebug("RequestApplicationService: Getting status for request {RequestId}", requestId);

        var aggregate = await _repository.FindByIdAsync(requestId, ct);
        if (aggregate is null)
        {
            _logger.LogDebug("RequestApplicationService: Request {RequestId} not found", requestId);
            return null;
        }

        var steps = aggregate.Steps
            .Select(s => new StepStatusItem(
                s.StepName,
                s.ExecutionStatus,
                s.StepRetries,
                s.StartedAt,
                s.CompletedAt,
                s.NextRetryAt,
                s.ErrorCode,
                s.ErrorMessage))
            .ToImmutableList();

        var response = new StatusResponse(
            aggregate.RequestId,
            aggregate.ClientId,
            aggregate.Strategy,
            aggregate.Status,
            aggregate.CurrentState,
            steps,
            aggregate.Result,
            aggregate.ErrorCode,
            aggregate.ErrorMessage,
            aggregate.CreatedAt,
            aggregate.UpdatedAt,
            aggregate.CompletedAt);

        _logger.LogDebug("RequestApplicationService: Status for request {RequestId} = {Status}", requestId, aggregate.Status);
        return response;
    }

    /// <inheritdoc/>
    public Task<PagedResult<RequestSummaryItem>> ListAsync(RequestFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return _repository.ListAsync(filter, ct);
    }

    /// <inheritdoc/>
    public Task<RequestStats> GetStatsAsync(StatsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return _repository.GetStatsAsync(filter, ct);
    }

    /// <inheritdoc/>
    public Task RetryLastStepAsync(Guid requestId, CancellationToken ct = default)
        => _retry.RetryLastStepAsync(requestId, ct);

    /// <inheritdoc/>
    public Task RetryStepAsync(Guid requestId, string stateName, CancellationToken ct = default)
        => _retry.RetryStepAsync(requestId, stateName, ct);

    /// <inheritdoc/>
    public async Task MarkRequestFailedAsync(Guid requestId, string errorCode, string errorMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode, nameof(errorCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage, nameof(errorMessage));

        var aggregate = await _repository.FindByIdAsync(requestId, ct)
            ?? throw new KeyNotFoundException($"Request '{requestId}' not found.");

        if (aggregate.Status == RequestStatus.Completed)
        {
            throw new InvalidOperationException(
                $"Request '{requestId}' is already Completed and cannot be failed.");
        }

        var error = new ErrorInfo(errorCode, errorMessage);
        aggregate.MarkFailed(error, DateTimeOffset.UtcNow);
        await _repository.SaveAsync(aggregate, ct);
    }

    /// <summary>
    /// Continues a workflow from a specific step by enqueuing it for execution.
    /// Validates that the strategy name matches the request's current strategy and that the request
    /// is in an appropriate state to continue. The new step is serialized, persisted in the aggregate,
    /// and enqueued for processing.
    /// </summary>
    /// <param name="requestId">The ID of the existing workflow request.</param>
    /// <param name="strategyName">The name of the strategy that should handle this continuation.</param>
    /// <param name="nextStep">The fully populated step request to enqueue for execution.</param>
    /// <param name="ct">Cancellation token for graceful shutdown.</param>
    /// <exception cref="KeyNotFoundException">Thrown when no request with the given ID exists.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request's strategy does not match <paramref name="strategyName"/>,
    /// when the request is in a terminal state (Completed or Failed), or when the strategy cannot be resolved.
    /// </exception>
    public async Task ContinueFromStepAsync(
        Guid requestId,
        string strategyName,
        StepRequest nextStep,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName, nameof(strategyName));
        ArgumentNullException.ThrowIfNull(nextStep, nameof(nextStep));

        _logger.LogDebug(
            "RequestApplicationService: Continuing request {RequestId} from step '{StepName}' with strategy '{StrategyName}'",
            requestId,
            nextStep.StateName,
            strategyName);

        var aggregate = await _repository.FindByIdAsync(requestId, ct)
            ?? throw new KeyNotFoundException($"Request '{requestId}' not found.");

        if (aggregate.Strategy != strategyName)
        {
            throw new InvalidOperationException(
                $"Request '{requestId}' uses strategy '{aggregate.Strategy}', but '{strategyName}' was provided. Strategy mismatch.");
        }

        var strategy = _registry.Resolve(strategyName);

        var now = DateTimeOffset.UtcNow;
        var messageType = nextStep.GetType().AssemblyQualifiedName
            ?? throw new InvalidOperationException(
                $"Cannot determine assembly-qualified name for step type '{nextStep.GetType().Name}'.");

        var inputData = JsonDocument.Parse(JsonSerializer.Serialize(nextStep));

        aggregate.RecordStepStarted(nextStep.StateName, inputData, messageType, now);
        await _repository.SaveAsync(aggregate, ct);
        await _queue.EnqueueAsync(nextStep, ct);

        _logger.LogInformation(
            "RequestApplicationService: Step '{StepName}' enqueued for continuation on request {RequestId}",
            nextStep.StateName,
            requestId);
    }
}