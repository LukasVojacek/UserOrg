namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Pure handler for a single domain state in a workflow strategy.
/// Each implementation performs exactly one unit of work for one state.
/// </summary>
/// <typeparam name="TRequest">
/// The typed <see cref="StepRequest"/> subtype this handler processes.
/// Resolved from DI by the worker using <c>IStepHandler&lt;TRequest&gt;</c>.
/// </typeparam>
/// <remarks>
/// Handlers have zero knowledge of flow routing, the queue, or other handlers.
/// All routing decisions belong to <see cref="IRequestStrategy.Transition"/>.
/// <para>
/// Throw <see cref="WorkflowEngine.Contracts.Exceptions.RetryableException"/> for transient failures —
/// the worker will schedule a retry with back-off.
/// </para>
/// <para>
/// Throw <see cref="WorkflowEngine.Contracts.Exceptions.PermanentException"/> for unrecoverable failures —
/// the worker will mark the flow <c>Failed</c> immediately.
/// </para>
/// </remarks>
public interface IStepHandler<in TRequest>
    where TRequest : StepRequest
{
    /// <summary>
    /// Executes the domain work for the given typed step message.
    /// </summary>
    Task<StepResult> HandleAsync(TRequest request, CancellationToken ct);
}