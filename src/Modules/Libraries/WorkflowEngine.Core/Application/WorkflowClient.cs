using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.Flows;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Application;

/// <summary>
/// Default <see cref="IWorkflowClient"/>: starts/submits via the application service, and implements
/// gate resume directly against the repository, strategy registry, and queue.
/// </summary>
internal sealed class WorkflowClient : IWorkflowClient
{
    private readonly IRequestApplicationService _appService;
    private readonly IRequestRepository _repository;
    private readonly IStrategyRegistry _registry;
    private readonly IQueuePort _queue;
    private readonly ICallbackPort _callbackPort;
    private readonly Dictionary<Type, IFlowDefinition> _flows;
    private readonly ILogger<WorkflowClient> _logger;

    public WorkflowClient(
        IRequestApplicationService appService,
        IRequestRepository repository,
        IStrategyRegistry registry,
        IQueuePort queue,
        ICallbackPort callbackPort,
        IEnumerable<IFlowDefinition> flows,
        ILogger<WorkflowClient> logger)
    {
        ArgumentNullException.ThrowIfNull(appService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(callbackPort);
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentNullException.ThrowIfNull(logger);

        _appService = appService;
        _repository = repository;
        _registry = registry;
        _queue = queue;
        _callbackPort = callbackPort;
        _flows = flows.ToDictionary(f => f.GetType());
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid> StartAsync<TFlow>(object input, SubmitOptions? options = null, CancellationToken ct = default)
        where TFlow : IFlowDefinition
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!_flows.TryGetValue(typeof(TFlow), out var flow))
        {
            throw new InvalidOperationException(
                $"Flow '{typeof(TFlow).Name}' is not registered. Call AddFlow<{typeof(TFlow).Name}>() at startup.");
        }

        var requestId = Guid.CreateVersion7();
        var initialStep = flow.CreateInitialStep(requestId, input);
        return await SubmitAsync(initialStep, options, ct);
    }

    /// <inheritdoc/>
    public async Task<Guid> SubmitAsync(StepRequest initialStep, SubmitOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initialStep);

        var opts = options ?? new SubmitOptions();
        var response = await _appService.SubmitAsync(initialStep, opts.ClientId, opts.CallbackUrl, opts.IdempotencyKey, ct);
        return response.RequestId;
    }

    /// <inheritdoc/>
    public async Task<ResumeResult> ResumeAsync<TSignal>(Guid requestId, TSignal signal, CancellationToken ct = default)
        where TSignal : class
    {
        ArgumentNullException.ThrowIfNull(signal);

        var aggregate = await _repository.FindByIdAsync(requestId, ct);
        if (aggregate is null)
        {
            return new ResumeResult(ResumeOutcome.RequestNotFound);
        }

        if (aggregate.Status != RequestStatus.AwaitingSignal)
        {
            return new ResumeResult(ResumeOutcome.NotAwaiting);
        }

        // A parked request always has a gate as its current state; guard defensively anyway.
        if (_registry.Resolve(aggregate.Strategy) is not IGatedStrategy gated
            || aggregate.CurrentState is null
            || !gated.TryGetGate(aggregate.CurrentState, out var gate))
        {
            return new ResumeResult(ResumeOutcome.NotAwaiting);
        }

        if (gate.SignalType != typeof(TSignal))
        {
            return new ResumeResult(ResumeOutcome.SignalTypeMismatch, gate.SignalType.Name);
        }

        var now = DateTimeOffset.UtcNow;
        var next = gate.Resume(signal, aggregate);

        switch (next)
        {
            case null:
            aggregate.MarkCompleted(null, now);
            break;
            case GateStep nextGate:
            aggregate.MarkAwaitingSignal(nextGate.GateName, now);
            break;
            default:
            aggregate.ResumeToInProgress(next.StateName, now);
            break;
        }

        await _repository.SaveAsync(aggregate, ct);

        switch (next)
        {
            case GateStep:
            _logger.LogInformation("Request {RequestId} resumed and re-parked at gate '{Gate}'",
                requestId, aggregate.CurrentState);
            break;

            case not null:
            await _queue.EnqueueAsync(next, ct);
            _logger.LogInformation("Request {RequestId} resumed; enqueued step '{Step}'",
                requestId, next.StateName);
            break;

            default:
            if (aggregate.CallbackUrl is not null)
            {
                await _callbackPort.SendAsync(aggregate, ct);
            }

            _logger.LogInformation("Request {RequestId} resumed to completion", requestId);
            break;
        }

        return new ResumeResult(ResumeOutcome.Resumed);
    }

    /// <inheritdoc/>
    public async Task<RetryResult> RetryAsync(Guid requestId, CancellationToken ct = default)
    {
        try
        {
            await _appService.RetryLastStepAsync(requestId, ct);
            return new RetryResult(RetryOutcome.Requeued);
        }
        catch (KeyNotFoundException)
        {
            return new RetryResult(RetryOutcome.RequestNotFound);
        }
        catch (InvalidOperationException)
        {
            return new RetryResult(RetryOutcome.NotFailed);
        }
    }

    /// <inheritdoc/>
    public async Task<RequestStatus?> GetStatusAsync(Guid requestId, CancellationToken ct = default)
    {
        var status = await _appService.GetStatusAsync(requestId, ct);
        return status?.Status;
    }
}