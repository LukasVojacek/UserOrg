using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Adapters;

/// <summary>
/// Non-generic bridge that allows the worker to call a typed
/// <see cref="IStepHandler{T}"/> without reflection.
/// </summary>
/// <remarks>
/// Registered in DI keyed by <c>typeof(TRequest)</c> via
/// <c>AddStepHandler&lt;TRequest, THandler&gt;()</c>.
/// The worker resolves it with
/// <c>GetRequiredKeyedService&lt;IStepHandlerInvoker&gt;(message.GetType())</c>.
/// </remarks>
internal interface IStepHandlerInvoker
{
    Task<StepResult> InvokeAsync(StepRequest request, CancellationToken ct);
}

/// <summary>
/// Generic adapter that bridges the untyped <see cref="IStepHandlerInvoker"/> interface
/// to the strongly-typed <see cref="IStepHandler{T}"/>.
/// The cast <c>(TRequest)request</c> is safe because the DI key guarantees the runtime
/// type of <paramref name="request"/> matches <typeparamref name="TRequest"/>.
/// </summary>
internal sealed class StepHandlerInvoker<TRequest> : IStepHandlerInvoker
    where TRequest : StepRequest
{
    private readonly IStepHandler<TRequest> _handler;

    public StepHandlerInvoker(IStepHandler<TRequest> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public Task<StepResult> InvokeAsync(StepRequest request, CancellationToken ct)
        => _handler.HandleAsync((TRequest)request, ct);
}