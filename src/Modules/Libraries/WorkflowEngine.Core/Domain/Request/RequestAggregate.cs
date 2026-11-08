using System.Text.Json;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Models;
namespace WorkflowEngine.Core.Domain.Request;

/// <summary>
/// Aggregate root for a workflow request.
/// Owns the flow-level state machine (status + current domain state) and the collection of step records.
/// All state-mutation methods are <c>internal</c> — only Core workers may advance the state.
/// Persistence adapters use the <see cref="Rehydrate"/> factory to reconstruct the aggregate from storage.
/// </summary>
public sealed class RequestAggregate : IRequestSnapshot
{
    private readonly List<RequestStep> _steps = [];

    /// <inheritdoc/>
    public Guid RequestId { get; private set; }

    /// <inheritdoc/>
    public string? ClientId { get; private set; }

    /// <inheritdoc/>
    public string Strategy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public RequestStatus Status { get; private set; }

    /// <inheritdoc/>
    public string? CurrentState { get; private set; }

    /// <inheritdoc/>
    public JsonDocument Payload { get; private set; } = JsonDocument.Parse("{}");

    /// <inheritdoc/>
    public string? CallbackUrl { get; private set; }

    /// <summary>Optional idempotency key associated with this request.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Optional aggregated result set on flow completion.</summary>
    public JsonDocument? Result { get; private set; }

    /// <summary>Short machine-readable error code; populated on failure.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Human-readable error message; populated on failure.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>When the request was first submitted.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last time any field on the aggregate was changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the flow transitioned to <see cref="RequestStatus.Completed"/> or <see cref="RequestStatus.Failed"/>.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// SQL Server row-version token. Set by the repository after every load; used for optimistic concurrency.
    /// The value is opaque to the domain — only the repository reads and writes it.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>Read-only ordered list of all step records belonging to this request.</summary>
    public IReadOnlyList<RequestStep> Steps => _steps.AsReadOnly();

    private RequestAggregate() { }

    /// <summary>
    /// Creates a brand-new request aggregate in <see cref="RequestStatus.Accepted"/> status.
    /// </summary>
    internal static RequestAggregate Create(
        string strategy,
        JsonDocument payload,
        string? clientId,
        string? callbackUrl,
        string? idempotencyKey,
        DateTimeOffset now,
        Guid? requestId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategy, nameof(strategy));
        ArgumentNullException.ThrowIfNull(payload, nameof(payload));

        return new RequestAggregate
        {
            RequestId = requestId ?? Guid.NewGuid(),
            Strategy = strategy,
            Payload = payload,
            ClientId = clientId,
            CallbackUrl = callbackUrl,
            IdempotencyKey = idempotencyKey,
            Status = RequestStatus.Accepted,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Reconstructs the aggregate from persisted storage.
    /// Used exclusively by <c>IRequestRepository</c> implementations.
    /// </summary>
    public static RequestAggregate Rehydrate(
        Guid requestId,
        string strategy,
        JsonDocument payload,
        RequestStatus status,
        string? currentState,
        string? clientId,
        string? callbackUrl,
        string? idempotencyKey,
        JsonDocument? result,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt,
        byte[]? rowVersion,
        IEnumerable<RequestStep> steps)
    {
        var aggregate = new RequestAggregate
        {
            RequestId = requestId,
            Strategy = strategy,
            Payload = payload,
            Status = status,
            CurrentState = currentState,
            ClientId = clientId,
            CallbackUrl = callbackUrl,
            IdempotencyKey = idempotencyKey,
            Result = result,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CompletedAt = completedAt,
            RowVersion = rowVersion,
        };

        aggregate._steps.AddRange(steps);

        return aggregate;
    }

    /// <summary>
    /// Records the start of a step execution.
    /// Transitions the flow to <see cref="RequestStatus.InProgress"/> on the first call.
    /// Reuses the step's live (non-<c>Completed</c>) row when it has one, so a retried step keeps a single
    /// row carrying its attempt count; appends a new <see cref="RequestStep"/> in <c>Running</c> status
    /// otherwise. A <c>Completed</c> row is never reused — <see cref="GetStepResult"/> and
    /// <see cref="IsStepCompleted"/> read it.
    /// </summary>
    internal void RecordStepStarted(
        string stateName,
        JsonDocument inputData,
        string messageType,
        DateTimeOffset now)
    {
        Status = RequestStatus.InProgress;
        CurrentState = stateName;
        UpdatedAt = now;

        var live = FindLiveStep(stateName);

        if (live is null)
        {
            _steps.Add(RequestStep.StartNew(RequestId, stateName, inputData, messageType, now));
            return;
        }

        live.RestartAttempt(inputData, messageType, now);
    }

    /// <summary>
    /// Returns the step's one row that has not completed, or <c>null</c> when the step has never run or has
    /// only completed rows. This is the row that carries the step's attempt count
    /// (<see cref="RequestStep.StepRetries"/>) across deliveries.
    /// </summary>
    public RequestStep? FindLiveStep(string stateName)
        => _steps.FindLast(s => s.StepName == stateName
                              && s.ExecutionStatus != Contracts.Models.StepExecutionStatus.Completed);

    /// <summary>
    /// Records the successful completion of a step and stores its output.
    /// Does not advance the flow state — call <see cref="SetCurrentState"/> or <see cref="MarkCompleted"/> after this.
    /// </summary>
    internal void RecordStepCompleted(string stateName, JsonDocument? data, DateTimeOffset now)
    {
        var step = FindLiveStep(stateName) ?? RequireStep(stateName);
        step.ExecutionStatus = Contracts.Models.StepExecutionStatus.Completed;
        step.Result = data;
        step.CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a permanent (non-retryable) failure for a step.
    /// Call <see cref="MarkFailed"/> afterwards to close the flow.
    /// </summary>
    internal void RecordStepFailed(string stateName, ErrorInfo error, DateTimeOffset now)
        => RecordStepFailed(RequireStep(stateName), error, now);

    /// <summary>
    /// Fails one specific <see cref="RequestStep"/> row rather than whichever row currently answers to its
    /// name. A step name that completes and runs again gets a second row, so the by-name overload can
    /// resolve to a row other than the one the caller acted on. A caller holding the exact row —
    /// <see cref="IRequestRepository.FindDueRetriesAsync"/> hands one back — must use this overload.
    /// </summary>
    internal void RecordStepFailed(RequestStep step, ErrorInfo error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(step);

        step.ExecutionStatus = Contracts.Models.StepExecutionStatus.Failed;
        step.ErrorCode = error.ErrorCode;
        step.ErrorMessage = error.ErrorMessage.Length > 2000 ? error.ErrorMessage.Substring(0, 2000) : error.ErrorMessage;
        step.CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a scheduled retry for a step after a transient failure.
    /// The worker will re-enqueue the step after <paramref name="nextRetryAt"/>.
    /// </summary>
    internal void RecordStepRetryScheduled(
        string stateName,
        ErrorInfo error,
        DateTimeOffset nextRetryAt,
        DateTimeOffset now)
    {
        var step = FindLiveStep(stateName) ?? RequireStep(stateName);
        step.ExecutionStatus = Contracts.Models.StepExecutionStatus.RetryScheduled;
        step.StepRetries += 1;
        step.NextRetryAt = nextRetryAt;
        step.ErrorCode = error.ErrorCode;
        step.ErrorMessage = error.ErrorMessage;
        UpdatedAt = now;
    }

    /// <summary>
    /// Advances the flow domain state after a step completes and the transition is known.
    /// </summary>
    internal void SetCurrentState(string nextStateName, DateTimeOffset now)
    {
        CurrentState = nextStateName;
        UpdatedAt = now;
    }

    /// <summary>
    /// Parks the request at a gate awaiting an external signal. Sets <see cref="RequestStatus.AwaitingSignal"/>
    /// and records the gate as the current state. The request leaves this state when a resume enqueues the
    /// next step (which moves it back to <see cref="RequestStatus.InProgress"/> via <see cref="RecordStepStarted"/>).
    /// The awaited signal type is not stored — it is recovered from the flow definition by the gate name.
    /// </summary>
    internal void MarkAwaitingSignal(string gateName, DateTimeOffset now)
    {
        CurrentState = gateName;
        Status = RequestStatus.AwaitingSignal;
        UpdatedAt = now;
    }

    /// <summary>
    /// Moves a request out of <see cref="RequestStatus.AwaitingSignal"/> back to
    /// <see cref="RequestStatus.InProgress"/> when a gate is resumed and the next step is enqueued.
    /// Flipping the status here (and saving with the row-version token) closes the idempotency window:
    /// a second resume sees <see cref="RequestStatus.InProgress"/> and is rejected as not-awaiting.
    /// </summary>
    internal void ResumeToInProgress(string nextStateName, DateTimeOffset now)
    {
        Status = RequestStatus.InProgress;
        CurrentState = nextStateName;
        UpdatedAt = now;
    }

    /// <summary>
    /// Closes the flow as <see cref="RequestStatus.Completed"/> after the terminal step succeeds.
    /// </summary>
    internal void MarkCompleted(JsonDocument? result, DateTimeOffset now)
    {
        Status = RequestStatus.Completed;
        Result = result;
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Closes the flow as <see cref="RequestStatus.Failed"/> after a permanent step failure or operator action.
    /// </summary>
    internal void MarkFailed(ErrorInfo error, DateTimeOffset now)
    {
        Status = RequestStatus.Failed;
        ErrorCode = error.ErrorCode;
        ErrorMessage = error.ErrorMessage;
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Resets a failed or retry-scheduled step for an operator's retry so it can be re-enqueued, restarting
    /// its retry budget (<see cref="RequestStep.StepRetries"/> back to zero).
    /// Clears error fields and retry scheduling metadata; sets status back to <c>Running</c> semantics
    /// (the worker will write <c>Running</c> again via <see cref="RecordStepStarted"/>).
    /// Also resets the flow status to <see cref="RequestStatus.InProgress"/>.
    /// A step is re-opened from two places and they differ in exactly this: an automatic retry and a startup
    /// recovery (<see cref="ResetStepForRecovery"/>) spend the budget and carry the count forward, so a
    /// worker crashing mid-attempt on every restart cannot retry without bound; only an operator's retry,
    /// through <c>RetryHandler</c>, is a decision to start the budget again.
    /// </summary>
    internal void ResetStepForRetry(string stateName, DateTimeOffset now)
    {
        ResolveLiveStep(stateName).ReopenForOperatorRetry();
        ReopenFlowForRetry(now);
    }

    /// <summary>
    /// Resets a step that startup recovery found stalled — its worker died mid-attempt — so it can be
    /// re-enqueued, keeping the retry budget it has already spent. See
    /// <see cref="ResetStepForRetry"/> for why only the operator's path restarts the count.
    /// </summary>
    internal void ResetStepForRecovery(string stateName, DateTimeOffset now)
    {
        ResolveLiveStep(stateName).ReopenForStartupRecovery();
        ReopenFlowForRetry(now);
    }

    private RequestStep ResolveLiveStep(string stateName)
        => FindLiveStep(stateName) ?? RequireStep(stateName);

    private void ReopenFlowForRetry(DateTimeOffset now)
    {
        if (Status == RequestStatus.Failed)
        {
            Status = RequestStatus.InProgress;
            ErrorCode = null;
            ErrorMessage = null;
            CompletedAt = null;
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Returns <c>true</c> when the step with <paramref name="stateName"/> has already completed successfully.
    /// Used by the worker to skip re-execution on crash-recovery replay.
    /// </summary>
    public bool IsStepCompleted(string stateName)
        => _steps.Exists(s => s.StepName == stateName
                            && s.ExecutionStatus == Contracts.Models.StepExecutionStatus.Completed);

    /// <summary>
    /// Returns the persisted result of the named step, or <c>null</c> if it has not completed or produced no data.
    /// </summary>
    public JsonDocument? GetStepResult(string stateName)
        => _steps.Find(s => s.StepName == stateName
                          && s.ExecutionStatus == Contracts.Models.StepExecutionStatus.Completed)
                 ?.Result;

    /// <inheritdoc/>
    public TResult ResultOf<TResult>(string stepName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName, nameof(stepName));

        var json = GetStepResult(stepName)
            ?? throw new InvalidOperationException(
                $"Step '{stepName}' on request {RequestId} has no completed result to read.");

        return json.Deserialize<TResult>()
            ?? throw new InvalidOperationException(
                $"Step '{stepName}' result on request {RequestId} deserialized to null for type '{typeof(TResult).Name}'.");
    }

    /// <summary>
    /// Returns the most recently started step, identified by the latest <c>StartedAt</c> timestamp.
    /// Returns <c>null</c> when no steps have been started yet.
    /// </summary>
    public RequestStep? GetLastStep()
        => _steps.Count == 0
            ? null
            : _steps.MaxBy(s => s.StartedAt);


    private RequestStep RequireStep(string stateName)
        => _steps.FindLast(s => s.StepName == stateName)
           ?? throw new InvalidOperationException(
               $"Step '{stateName}' not found on request {RequestId}. Ensure RecordStepStarted was called before RecordStepCompleted/RecordStepFailed.");
}