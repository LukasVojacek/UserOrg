using Core.DataAccess.ResourceRequests;

using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Shared plumbing for the assign-resource flow's step handlers: loading the projection row (a missing row is a
/// permanent failure, since the flow cannot proceed without it) and classifying a step's side effect (a
/// notification send or a database write) so a transient failure retries while a permanent one stops the step.
/// </summary>
internal static class FlowHandlerHelpers
{
    internal static async Task<ProjectResourceRequest> RequireBySlugAsync(
        this IResourceRequestProjectionStore store, string slug, CancellationToken ct) =>
        await store.GetBySlugAsync(slug, ct)
        ?? throw new PermanentException("resource_request.row_missing", $"Projection row '{slug}' not found.");

    /// <summary>
    /// Runs <paramref name="send"/> and wraps any failure in <see cref="RetryableException"/>, except two that
    /// propagate unwrapped: <see cref="PermanentException"/>, and <see cref="ResourceRequestRowMissingException"/>,
    /// because a deleted projection row never comes back and retrying it only burns <c>MaxStepRetries</c>.
    /// The carve-out is on that dedicated type, not on <see cref="InvalidOperationException"/>: SqlClient
    /// connection-pool exhaustion is also an <see cref="InvalidOperationException"/> and must still retry
    /// (ADR-0087).
    /// </summary>
    internal static async Task SendOrRetryAsync(string failureCode, string failureMessage, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not PermanentException and not ResourceRequestRowMissingException)
        {
            throw new RetryableException(failureCode, failureMessage, ex);
        }
    }
}