namespace Heappe.Features.GetLocationPartitions.Response;

/// <summary>
/// Represents a single location partition in the response.
/// </summary>
/// <param name="PartitionName">The partition name.</param>
/// <param name="AggregationName">The aggregation name (queue name).</param>
/// <param name="LocationName">The location name.</param>
/// <param name="ProviderName">The provider name.</param>
public sealed record LocationPartitionResponse(
    string PartitionName,
    string AggregationName,
    string LocationName,
    string ProviderName
);