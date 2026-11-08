using System.Collections.Immutable;

namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Generic paged result wrapper.
/// </summary>
public sealed record PagedResult<T>(
    ImmutableList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);