namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Represents a location entry synchronized from an external system.
/// </summary>
/// <param name="Id">Unique identifier of the location.</param>
/// <param name="Name">Name of the location.</param>
/// <param name="LoginEndpoint">Authentication endpoint URL for the location.</param>
/// <param name="HeappeInstance">Associated Heappe instance synchronization entry.</param>
public sealed record LocationSyncEntry
(
    Guid Id,
    string Name,
    string LoginEndpoint,
    HeappeInstanceSyncEntry HeappeInstance
);