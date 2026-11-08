namespace Heappe.Contracts.CommandTemplatePermissions.Contract;

/// <summary>
/// Represents the allocation details for a specific location, including its identifier, name, optional aggregation, and
/// associated Heappe service instance.
/// </summary>
/// <remarks>Use this record to encapsulate all relevant information required to identify and manage a location's
/// allocation within the system. This type is typically used in scenarios involving resource management or integration
/// with the Heappe service.</remarks>
/// <param name="LocationId">The unique identifier of the location for which the allocation information is provided.</param>
/// <param name="LocationName">The human-readable name of the location associated with this allocation.</param>
/// <param name="AggregationName">The name of the aggregation to which the location belongs, or null if the location is not part of an aggregation.</param>
/// <param name="HeappeInstance">The Heappe service instance entry associated with this location allocation.</param>
public sealed record LocationAllocationInfo
(
    Guid LocationId,
    string LocationName,
    string? AggregationName,
    HeappeInstanceEntry HeappeInstance
);