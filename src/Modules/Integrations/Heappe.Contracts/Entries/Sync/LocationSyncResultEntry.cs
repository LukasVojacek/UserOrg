using Lukdrasil.Error;

namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Outcome of synchronizing command templates for a single location aggregation
/// (one location + queue/aggregation pair) within a HEAppE instance.
/// </summary>
/// <param name="LocationName">The name of the synchronized location.</param>
/// <param name="AggregationName">The optional queue/aggregation name for the location. This value can be null if no aggregation is specified.</param>
/// <param name="HeappeInstanceUri">The URI of the HEAppE instance the location belongs to.</param>
/// <param name="IsSuccess">Whether synchronization succeeded for this location.</param>
/// <param name="Errors">The errors encountered while synchronizing this location, if any.</param>
public sealed record LocationSyncResultEntry(
    string LocationName,
    string? AggregationName,
    Uri HeappeInstanceUri,
    bool IsSuccess,
    IReadOnlyList<Error> Errors);