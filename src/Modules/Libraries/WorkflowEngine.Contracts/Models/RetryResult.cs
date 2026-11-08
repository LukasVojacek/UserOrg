namespace WorkflowEngine.Contracts.Models;

/// <summary>Outcome of attempting to retry a failed or stuck request.</summary>
public enum RetryOutcome
{
    /// <summary>The last step was reset and re-enqueued.</summary>
    Requeued,

    /// <summary>The request is not in a retryable state (e.g. already completed, or no failed step).</summary>
    NotFailed,

    /// <summary>No request with the given id exists.</summary>
    RequestNotFound,
}

/// <summary>Result of <c>IWorkflowClient.RetryAsync</c>.</summary>
/// <param name="Outcome">What happened.</param>
public sealed record RetryResult(RetryOutcome Outcome);