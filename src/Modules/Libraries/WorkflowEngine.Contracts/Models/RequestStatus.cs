namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Flow-level status of a request aggregate.
/// Represents where the business flow is as a whole — not the technical outcome of an individual step.
/// </summary>
public enum RequestStatus
{
    /// <summary>Request has been persisted; the initial step has been enqueued but not yet started.</summary>
    Accepted,

    /// <summary>At least one step is running or queued.</summary>
    InProgress,

    /// <summary>
    /// The flow is parked at a gate, awaiting an external signal before it can proceed.
    /// This is an execution-lifecycle state ("the engine cannot proceed without external input"),
    /// which a UI typically surfaces as "action required". Business milestones are read from
    /// <see cref="WorkflowEngine.Contracts.Abstractions.IRequestSnapshot.CurrentState"/>, not from this status.
    /// </summary>
    AwaitingSignal,

    /// <summary>All steps completed successfully and the terminal transition returned <c>null</c>.</summary>
    Completed,

    /// <summary>A step encountered a permanent failure or exhausted its retry budget.</summary>
    Failed,
}