using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;

namespace WorkflowEngine.Contracts.Ports;

/// <summary>
/// Small runtime facade for driving flows: start a request, resume it past a gate, retry a failed step,
/// and read its status. The authoring surface (defining flows) is <see cref="IFlowDefinition"/> /
/// <see cref="IFlowBuilder"/>; this is the operational surface.
/// </summary>
public interface IWorkflowClient
{
    /// <summary>
    /// Starts a new request for <typeparamref name="TFlow"/>. The flow builds the initial step from
    /// <paramref name="input"/> via <see cref="IFlowDefinition.CreateInitialStep"/>.
    /// </summary>
    /// <returns>The new request id.</returns>
    Task<Guid> StartAsync<TFlow>(object input, SubmitOptions? options = null, CancellationToken ct = default)
        where TFlow : IFlowDefinition;

    /// <summary>
    /// Low-level escape hatch: starts a request from a fully built initial step. The owning flow is
    /// resolved from the step's runtime type.
    /// </summary>
    /// <returns>The request id (the initial step's <see cref="StepRequest.RequestId"/>).</returns>
    Task<Guid> SubmitAsync(StepRequest initialStep, SubmitOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Delivers a signal to the gate the request is currently parked at. The engine verifies the request is
    /// awaiting a signal and that <typeparamref name="TSignal"/> matches the gate's declared signal type,
    /// then routes the request out of the gate.
    /// </summary>
    Task<ResumeResult> ResumeAsync<TSignal>(Guid requestId, TSignal signal, CancellationToken ct = default)
        where TSignal : class;

    /// <summary>Operator recovery: re-enqueues the last step of a failed or stuck request. Distinct from resume.</summary>
    Task<RetryResult> RetryAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Returns the current status of a request, or <c>null</c> if it does not exist.</summary>
    Task<RequestStatus?> GetStatusAsync(Guid requestId, CancellationToken ct = default);
}