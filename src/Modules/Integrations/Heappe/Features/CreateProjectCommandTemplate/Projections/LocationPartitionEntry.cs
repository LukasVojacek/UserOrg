namespace Heappe.Features.CreateProjectCommandTemplate.Projections;

/// <summary>
/// Represents a location partition entry with its identifier, name, and optional aggregation information.
/// </summary>
/// <param name="PartitionId">Unique identifier of the partition.</param>
/// <param name="Name">Name of the partition.</param>
/// <param name="AggregationName">Optional aggregation name associated with the partition.</param>
internal sealed record LocationPartitionEntry
(
    Guid PartitionId,
    string Name,
    string? AggregationName
);