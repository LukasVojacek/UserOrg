using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Represents information about a specific location allocation, including its identifier, name, optional aggregation
/// group, and associated Heappe instance.
/// </summary>
/// <remarks>Use this record to encapsulate all relevant details required for location-based allocation scenarios,
/// such as resource scheduling or reporting. The aggregation name can be used to group multiple locations under a
/// common category for summary operations.</remarks>
/// <param name="LocationId">The unique identifier of the location for which the allocation information is provided.</param>
/// <param name="LocationName">The name of the location associated with this allocation entry.</param>
/// <param name="LoginEndpoint">The login endpoint for the location.</param>
/// <param name="LocationType">The type identifier of the location.</param>
/// <param name="AggregationName">The optional name used to group this location for aggregation purposes. May be null if aggregation is not
/// applicable.</param>
/// <param name="HeappeInstance">The Heappe instance entry associated with this location allocation.</param>
public sealed record LocationAllocationInfoSyncEntry
(
    Guid LocationId,
    string LocationName,
    string LoginEndpoint,
    ELocationType LocationType,
    string? AggregationName,
    HeappeInstanceSyncEntry HeappeInstance
);