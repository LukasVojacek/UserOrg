namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Filter parameters for aggregated statistics queries.
/// </summary>
public sealed record StatsFilter(
    string? Strategy = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);