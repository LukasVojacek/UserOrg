namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Represents a resource entry that contains identification, descriptive information, availability dates, and
/// associated location allocations.
/// </summary>
/// <remarks>Use this record to encapsulate all relevant details about a resource, including its identity,
/// descriptive metadata, availability period, and location allocations. This facilitates consistent access and
/// management of resource-related data across the application.</remarks>
/// <param name="Id">The unique identifier for the resource entry.</param>
/// <param name="Name">The name of the resource, used as the primary display identifier.</param>
/// <param name="Description">An optional description that provides additional details about the resource.</param>
/// <param name="StartDate">The optional date and time when the resource becomes available. If null, the resource is available immediately.</param>
/// <param name="EndDate">The optional date and time when the resource is no longer available. If null, the resource remains available
/// indefinitely.</param>
/// <param name="Locations">A read-only list of location allocation information associated with the resource.</param>
public record ResourceInfoSyncEntry
(
    Guid Id,
    string Name,
    string? Description,
    DateTime? StartDate,
    DateTime? EndDate,
    IReadOnlyList<LocationAllocationInfoSyncEntry> Locations
);