namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Sentinel "next step" emitted by a transition that routes into a Gate. It carries no work:
/// when the worker sees a <see cref="GateStep"/> as the next step, it parks the request in
/// <see cref="WorkflowEngine.Contracts.Models.RequestStatus.AwaitingSignal"/> at the named gate instead of
/// dispatching a handler. The request leaves the gate when a matching signal is delivered (resume), at
/// which point the gate's signal type is recovered from the flow definition — so it need not be persisted.
/// </summary>
/// <param name="RequestId">The owning request.</param>
/// <param name="GateName">The gate the request is parked at; also used as the current state name.</param>
public sealed record GateStep(Guid RequestId, string GateName)
    : StepRequest(RequestId, GateName);