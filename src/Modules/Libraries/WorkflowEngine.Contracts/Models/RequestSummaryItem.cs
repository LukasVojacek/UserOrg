namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Summary item returned in a paged list of requests.
/// </summary>
public sealed record RequestSummaryItem(
    Guid RequestId,
    string? ClientId,
    string Strategy,
    RequestStatus Status,
    string? CurrentState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);