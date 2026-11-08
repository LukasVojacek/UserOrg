namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Filter parameters for paged request history queries.
/// All parameters are optional — omitting a parameter means "no filter on that field".
/// </summary>
public sealed record RequestFilter(
    string? ClientId = null,
    string? Strategy = null,
    RequestStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20);