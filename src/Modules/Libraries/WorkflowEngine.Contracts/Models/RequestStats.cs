using System.Collections.Immutable;

namespace WorkflowEngine.Contracts.Models;

/// <summary>
/// Per-strategy breakdown within <see cref="RequestStats"/>.
/// </summary>
public sealed record StrategyStats(
    string Strategy,
    int Total,
    int Completed,
    int Failed,
    int InProgress,
    int Accepted);

/// <summary>
/// Aggregated statistics returned by <c>IRequestApplicationService.GetStatsAsync</c>.
/// </summary>
public sealed record RequestStats(
    int Total,
    int Completed,
    int Failed,
    int InProgress,
    int Accepted,
    DateTimeOffset? LastProcessedAt,
    ImmutableList<StrategyStats> ByStrategy);