namespace WorkflowEngine.Contracts.Models;

/// <summary>Outcome of attempting to resume a request parked at a gate.</summary>
public enum ResumeOutcome
{
    /// <summary>The signal was accepted; the request advanced out of the gate.</summary>
    Resumed,

    /// <summary>The request exists but is not currently parked at a gate.</summary>
    NotAwaiting,

    /// <summary>The supplied signal type does not match the gate's declared signal type.</summary>
    SignalTypeMismatch,

    /// <summary>No request with the given id exists.</summary>
    RequestNotFound,
}

/// <summary>
/// Result of <c>IWorkflowClient.ResumeAsync</c>. The non-happy outcomes are returned as values rather than
/// thrown, because resuming a request that is not parked (or with the wrong signal type) is an expected
/// conflict, not an exceptional error on the happy path.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="AwaitedSignalType">
/// On <see cref="ResumeOutcome.SignalTypeMismatch"/>, the signal type the gate actually awaits; otherwise <c>null</c>.
/// </param>
public sealed record ResumeResult(ResumeOutcome Outcome, string? AwaitedSignalType = null);