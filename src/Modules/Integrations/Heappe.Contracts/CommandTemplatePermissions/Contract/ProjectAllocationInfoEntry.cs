namespace Heappe.Contracts.CommandTemplatePermissions.Contract;

/// <summary>
/// Represents project allocation information, including the project's unique identifier, short name, and associated
/// resource location allocations.
/// </summary>
/// <remarks>Use this record to encapsulate and transfer essential allocation details for a project, enabling
/// efficient resource management and tracking across different locations.</remarks>
/// <param name="Id">The unique identifier of the project.</param>
/// <param name="ShortName">The short name of the project, typically used for display or reference purposes.</param>
/// <param name="Resources">A read-only list of resource allocation information entries, each representing a specific resource allocated to
/// the project.</param>
public sealed record ProjectAllocationInfoEntry
(
    Guid Id,
    string ShortName,
    IReadOnlyList<ResourceAllocationInfoEntry> Resources
);