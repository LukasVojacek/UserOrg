using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;

namespace WorkflowEngine.Contracts.Ports;

/// <summary>
/// Driving port — the primary entry point for all external interactions with the workflow module.
/// The module exposes no HTTP endpoints; API adapters call this interface exclusively.
/// </summary>
public interface IRequestApplicationService
{
    /// <summary>
    /// Submits a new request by providing the initial step directly.
    /// The engine resolves the owning strategy via <see cref="IRequestStrategy.InitialStepType"/>,
    /// serialises the step as payload, and enqueues it for immediate execution.
    /// <para>
    /// If an aggregate already exists for <see cref="StepRequest.RequestId"/>, the call is
    /// treated as an idempotent re-submission and the existing status is returned without side effects.
    /// </para>
    /// <para>
    /// This is the recommended submission method for type-safe, production workflows. Callers provide
    /// a fully populated step instance and the engine handles routing, persistence, and execution automatically.
    /// </para>
    /// </summary>
    /// <param name="initialStep">
    /// The first (mandatory) step of the workflow, serialised as the request payload.
    /// Must be a concrete instance of <see cref="StepRequest"/>. The engine uses the step's runtime type
    /// to identify the owning <see cref="IRequestStrategy"/> via <see cref="IStrategyRegistry.ResolveByStepType"/>.
    /// <see cref="StepRequest.RequestId"/> becomes the unique identifier of the entire flow instance.
    /// Use <see cref="Guid.NewGuid"/> for each fresh request; reuse the same ID for idempotent re-submission
    /// of an existing request (see also <paramref name="idempotencyKey"/>).
    /// </param>
    /// <param name="clientId">
    /// Optional identifier of the caller submitting this request. Typically a username, service name, or API client ID.
    /// Used for audit trails and filtering requests by submitter. If null or empty, the request is recorded as
    /// submitted by an anonymous/system caller. Useful for multi-tenant or RBAC scenarios where you need to track
    /// which user or system initiated a workflow.
    /// </param>
    /// <param name="callbackUrl">
    /// Optional opaque callback target to be invoked when the workflow reaches a terminal state (Completed or Failed).
    /// The semantics and format are implementation-specific: may be an HTTP URL, a message queue name, a topic name,
    /// a gRPC endpoint, or any other target understood by the <see cref="ICallbackPort"/> adapter.
    /// If provided, the engine guarantees at-least-once delivery of the <see cref="StatusResponse"/> to this target.
    /// If null or empty, no callback is performed (fire-and-forget submission).
    /// </param>
    /// <param name="idempotencyKey">
    /// Optional unique key for deduplication across submission attempts. If the same key is submitted within
    /// its configured TTL window, the engine returns the previously stored response without re-executing the workflow.
    /// Typically a business correlation ID, transaction ID, or message ID from an upstream system.
    /// Enables safe retry logic: callers may retry on transient network failures without accidentally triggering
    /// duplicate workflow executions. If null or empty, no idempotency check is performed (each submission is treated as new).
    /// </param>
    /// <param name="ct">
    /// Cancellation token to signal graceful shutdown or timeout of the submission operation.
    /// Does not cancel the workflow execution itself — only the synchronous submission call.
    /// Defaults to <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A <see cref="SubmitResponse"/> containing the flow ID and current status. Returns immediately after
    /// the request is persisted and enqueued, not after the workflow completes. Poll <see cref="GetStatusAsync"/>
    /// to monitor progress.
    /// </returns>
    Task<SubmitResponse> SubmitAsync(
        StepRequest initialStep,
        string? clientId = null,
        string? callbackUrl = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    /// <summary>Returns the full status snapshot for a single request, including per-step details.</summary>
    /// <returns><c>null</c> when no request with the given ID exists.</returns>
    Task<StatusResponse?> GetStatusAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Returns a paged history of requests matching the given filter criteria.</summary>
    Task<PagedResult<RequestSummaryItem>> ListAsync(RequestFilter filter, CancellationToken ct = default);

    /// <summary>Returns aggregated statistics for requests matching the given filter criteria.</summary>
    Task<RequestStats> GetStatsAsync(StatsFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Operator operation: re-enqueues the last step of a failed or stuck request.
    /// </summary>
    Task RetryLastStepAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Operator operation: re-enqueues a specific named step of a failed or stuck request.
    /// </summary>
    Task RetryStepAsync(Guid requestId, string stateName, CancellationToken ct = default);

    /// <summary>
    /// Operator operation: administratively closes a stuck or failed flow with a supplied error.
    /// </summary>
    Task MarkRequestFailedAsync(Guid requestId, string errorCode, string errorMessage, CancellationToken ct = default);

    /// <summary>
    /// Continues a workflow from a specific step by enqueuing it for execution.
    /// Used when an operator wants to resume a workflow at a particular step with new data.
    /// The step is serialized, persisted, and enqueued for processing by the appropriate handler.
    /// </summary>
    /// <param name="requestId">The ID of the existing workflow request to continue.</param>
    /// <param name="strategyName">The name of the strategy that should handle this step.</param>
    /// <param name="nextStep">The fully populated step request with data to be executed.</param>
    /// <param name="ct">Cancellation token for graceful shutdown.</param>
    /// <returns>A completed task when the step has been persisted and enqueued.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no request with the given ID exists.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request's strategy does not match the provided strategy name
    /// or when the request is in a terminal state.
    /// </exception>
    Task ContinueFromStepAsync(
        Guid requestId,
        string strategyName,
        StepRequest nextStep,
        CancellationToken ct = default);
}