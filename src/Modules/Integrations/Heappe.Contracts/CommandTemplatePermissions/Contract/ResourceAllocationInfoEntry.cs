namespace Heappe.Contracts.CommandTemplatePermissions.Contract;

/// <summary>
/// Represents a resource allocation entry, including its unique identifier, name, and associated location allocation
/// details.
/// </summary>
/// <param name="Id">The unique identifier for the resource allocation entry.</param>
/// <param name="Name">The name of the resource allocation entry.</param>
/// <param name="Locations">A read-only list of location allocation information associated with this resource allocation entry.</param>
public sealed record ResourceAllocationInfoEntry
(
    Guid Id,
    string Name,
    IReadOnlyList<LocationAllocationInfo> Locations
);