using System.Text.Json;

using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Features.Submit;

/// <summary>
/// Handles request submission via a directly provided initial step.
/// <list type="number">
///   <item>Idempotency check by key or existing <see cref="StepRequest.RequestId"/>.</item>
///   <item>Strategy lookup via <see cref="IStrategyRegistry.ResolveByStepType"/>.</item>
///   <item>Creates and persists a new <see cref="RequestAggregate"/> using the step's own <c>RequestId</c>.</item>
///   <item>Enqueues the step directly — no <c>CreateInitialMessage</c> call needed.</item>
/// </list>
/// </summary>
internal sealed class SubmitHandler
{
    private readonly IRequestRepository _repository;
    private readonly IQueuePort _queue;
    private readonly IStrategyRegistry _registry;
    private readonly ILogger<SubmitHandler> _logger;

    public SubmitHandler(IRequestRepository repository, IQueuePort queue, IStrategyRegistry registry, ILogger<SubmitHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _queue = queue;
        _registry = registry;
        _logger = logger;
    }

    public async Task<SubmitResponse> HandleAsync(
        StepRequest initialStep,
        string? clientId,
        string? callbackUrl,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(initialStep);

        _logger.LogDebug(
            "SubmitHandler: Submitting request {RequestId} with step type {StepType}",
            initialStep.RequestId,
            initialStep.GetType().Name);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cached = await _repository.FindByIdempotencyKeyAsync(idempotencyKey, ct);
            if (cached is not null)
            {
                _logger.LogInformation(
                    "SubmitHandler: Idempotency hit for key '{IdempotencyKey}' → returning existing request {RequestId}",
                    idempotencyKey,
                    cached.RequestId);
                return new SubmitResponse(cached.RequestId, cached.Status);
            }
        }

        // Idempotency check by RequestId — return existing aggregate without side effects
        var existing = await _repository.FindByIdAsync(initialStep.RequestId, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "SubmitHandler: Request {RequestId} already exists, returning without re-submission",
                initialStep.RequestId);
            return new SubmitResponse(existing.RequestId, existing.Status);
        }

        var strategy = _registry.ResolveByStepType(initialStep.GetType());
        _logger.LogDebug(
            "SubmitHandler: Resolved strategy '{Strategy}' for step type {StepType}",
            strategy.Name,
            initialStep.GetType().Name);

        // Serialise the step itself as the aggregate payload so strategies can read it in Transition
        var payload = JsonSerializer.SerializeToDocument(initialStep, initialStep.GetType());
        var now = DateTimeOffset.UtcNow;

        var aggregate = RequestAggregate.Create(
            strategy.Name,
            payload,
            clientId,
            callbackUrl,
            idempotencyKey,
            now,
            requestId: initialStep.RequestId);

        await _repository.AddAsync(aggregate, ct);
        _logger.LogDebug("SubmitHandler: Request {RequestId} persisted to repository", aggregate.RequestId);

        await _queue.EnqueueAsync(initialStep, ct);
        _logger.LogDebug("SubmitHandler: Initial step '{StepType}' enqueued for request {RequestId}",
            initialStep.GetType().Name,
            aggregate.RequestId);

        _logger.LogInformation(
            "SubmitHandler: Request {RequestId} successfully submitted with strategy '{Strategy}'",
            aggregate.RequestId,
            strategy.Name);

        return new SubmitResponse(aggregate.RequestId, aggregate.Status);
    }
}