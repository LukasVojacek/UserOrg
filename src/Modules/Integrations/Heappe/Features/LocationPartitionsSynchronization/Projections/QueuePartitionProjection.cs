namespace Heappe.Features.LocationPartitionsSynchronization.Projections;


/// <summary>
/// Represents a queue partition projection used for matching queue and aggregation name combinations.
/// </summary>
/// <param name="QueueName">The queue name.</param>
/// <param name="AggregationName">The optional aggregation name, or <see langword="null"/> if no aggregation is specified.</param>
internal sealed record QueuePartitionProjection
(
    string QueueName,
    string? AggregationName
)
{
    internal bool IsMatch(string queueName, string? aggregationName)
        => queueName == QueueName
        && string.Equals(aggregationName ?? string.Empty, AggregationName ?? string.Empty, StringComparison.Ordinal);
}