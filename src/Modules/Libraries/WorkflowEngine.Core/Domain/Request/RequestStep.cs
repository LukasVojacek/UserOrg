using System.Text.Json;

using WorkflowEngine.Contracts.Models;

namespace WorkflowEngine.Core.Domain.Request;

/// <summary>
/// Represents a step name's execution row within a <see cref="RequestAggregate"/>.
/// A step name has at most one live (non-<c>Completed</c>) row per request, and that row carries the
/// attempt count across retries; each completed execution leaves its own <c>Completed</c> row.
/// </summary>
public sealed class RequestStep
{
    /// <summary>Server-generated unique identifier of this step record.</summary>
    public Guid StepId { get; private set; }

    /// <summary>Parent request identifier.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>Domain state name as defined by the strategy (e.g. <c>"CallHEAppE"</c>).</summary>
    public string StepName { get; private set; } = string.Empty;

    /// <summary>Current technical execution status of this attempt.</summary>
    public StepExecutionStatus ExecutionStatus { get; internal set; }

    /// <summary>Assembly-qualified CLR type name of the <see cref="WorkflowEngine.Contracts.Abstractions.StepRequest"/> message.
    /// Used to reconstruct the message for deterministic replay and crash recovery.</summary>
    public string MessageType { get; private set; } = string.Empty;

    /// <summary>Serialized <see cref="WorkflowEngine.Contracts.Abstractions.StepRequest"/>; persisted before handler invocation to enable replay.</summary>
    public JsonDocument? InputData { get; private set; }

    /// <summary>Number of retry attempts made for this step (not counting the initial attempt).</summary>
    public int StepRetries { get; internal set; }

    /// <summary>Scheduled time for the next retry attempt; <c>null</c> when no retry is pending.</summary>
    public DateTimeOffset? NextRetryAt { get; internal set; }

    /// <summary>Worker instance identifier holding the current lease; <c>null</c> when unlocked.</summary>
    public string? LockedBy { get; internal set; }

    /// <summary>Lease expiry timestamp; <c>null</c> when unlocked. Expired leases are treated as released.</summary>
    public DateTimeOffset? LockedUntil { get; internal set; }

    /// <summary>Time when <c>RecordStepStarted</c> was called.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Time when <c>RecordStepCompleted</c> was called; <c>null</c> for non-terminal statuses.</summary>
    public DateTimeOffset? CompletedAt { get; internal set; }

    /// <summary>JSON data returned by the handler; <c>null</c> when the step has not yet completed or produced no data.</summary>
    public JsonDocument? Result { get; internal set; }

    /// <summary>Short machine-readable error code; populated only on failure.</summary>
    public string? ErrorCode { get; internal set; }

    /// <summary>Human-readable error description; populated only on failure.</summary>
    public string? ErrorMessage { get; internal set; }

    private RequestStep() { }

    /// <summary>
    /// Creates a new step record at the moment execution starts.
    /// Called by <see cref="RequestAggregate.RecordStepStarted"/> after the lease is acquired.
    /// </summary>
    internal static RequestStep StartNew(
        Guid requestId,
        string stepName,
        JsonDocument inputData,
        string messageType,
        DateTimeOffset startedAt)
    {
        return new RequestStep
        {
            StepId = Guid.CreateVersion7(),
            RequestId = requestId,
            StepName = stepName,
            ExecutionStatus = StepExecutionStatus.Running,
            MessageType = messageType,
            InputData = inputData,
            StepRetries = 0,
            StartedAt = startedAt,
        };
    }

    /// <summary>
    /// Re-opens this row for another execution attempt, carrying <see cref="StepRetries"/> forward.
    /// The request keeps one live row per step name, so the attempt counter the retry budget reads has to
    /// survive the next delivery rather than restart at zero on a freshly appended row.
    /// The lease fields are left alone: the caller has just acquired the lease this attempt runs under.
    /// </summary>
    internal void RestartAttempt(JsonDocument inputData, string messageType, DateTimeOffset startedAt)
    {
        ClearAttemptOutcome();
        MessageType = messageType;
        InputData = inputData;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Re-opens this row for a manually requested retry and releases any lease it still holds, so the
    /// re-enqueued delivery can lease it again. This resets <see cref="StepRetries"/>.
    /// Two other paths re-open a row and must not: an automatic retry (<see cref="RestartAttempt"/>) and a
    /// startup recovery of a step whose worker died mid-attempt
    /// (<see cref="ReopenForStartupRecovery"/>). Both are the budget being spent and carry the count
    /// forward; only an operator's retry is a decision to start the budget again, so a crash loop cannot
    /// hand a step an endless supply of attempts.
    /// </summary>
    internal void ReopenForOperatorRetry()
    {
        ReopenRow();
        StepRetries = 0;
    }

    /// <summary>
    /// Re-opens this row after startup recovery found it stalled, carrying <see cref="StepRetries"/>
    /// forward: the crashed attempt spent budget, so the recovered delivery continues the same budget
    /// rather than a fresh one. Releases the expired lease so the re-enqueued delivery can lease it again.
    /// </summary>
    internal void ReopenForStartupRecovery() => ReopenRow();

    private void ReopenRow()
    {
        ClearAttemptOutcome();
        LockedBy = null;
        LockedUntil = null;
    }

    private void ClearAttemptOutcome()
    {
        ExecutionStatus = StepExecutionStatus.Running;
        NextRetryAt = null;
        CompletedAt = null;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// Rehydrates a step from persisted storage.
    /// Used exclusively by <c>IRequestRepository</c> implementations when loading an aggregate.
    /// </summary>
    public static RequestStep Rehydrate(
        Guid stepId,
        Guid requestId,
        string stepName,
        StepExecutionStatus executionStatus,
        string messageType,
        JsonDocument? inputData,
        int stepRetries,
        DateTimeOffset? nextRetryAt,
        string? lockedBy,
        DateTimeOffset? lockedUntil,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        JsonDocument? result,
        string? errorCode,
        string? errorMessage)
    {
        return new RequestStep
        {
            StepId = stepId,
            RequestId = requestId,
            StepName = stepName,
            ExecutionStatus = executionStatus,
            MessageType = messageType,
            InputData = inputData,
            StepRetries = stepRetries,
            NextRetryAt = nextRetryAt,
            LockedBy = lockedBy,
            LockedUntil = lockedUntil,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Result = result,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
    }
}