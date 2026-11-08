namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Execution status of a <c>RequestSteps</c> row. The attempts of one live step share a row, so this is the
/// technical outcome of that step's current execution — distinct from the flow-level <see cref="RequestStatus"/>.
/// </summary>
public enum StepExecutionStatus
{
    /// <summary>Step is actively executing; <c>RecordStepStarted</c> was called and persisted before handler invocation.</summary>
    Running,

    /// <summary>Handler returned <see cref="WorkflowEngine.Contracts.Abstractions.StepResult"/> successfully.</summary>
    Completed,

    /// <summary>Handler threw a permanent (non-retryable) exception, or the retry budget was exhausted.</summary>
    Failed,

    /// <summary>Handler threw a <see cref="WorkflowEngine.Contracts.Exceptions.RetryableException"/>; re-enqueue is pending after back-off.</summary>
    RetryScheduled,
}