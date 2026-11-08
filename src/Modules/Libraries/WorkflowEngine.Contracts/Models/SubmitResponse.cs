namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Returned by <c>IRequestApplicationService.SubmitAsync</c> after a successful or idempotent submission.
/// </summary>
/// <param name="RequestId">Server-generated unique identifier of the request.</param>
/// <param name="Status">
/// <see cref="RequestStatus.Accepted"/> for a newly created request,
/// or <see cref="RequestStatus.Accepted"/> / <see cref="RequestStatus.InProgress"/> / <see cref="RequestStatus.Completed"/>
/// when returned from an idempotent duplicate hit.
/// </param>
public sealed record SubmitResponse(Guid RequestId, RequestStatus Status);