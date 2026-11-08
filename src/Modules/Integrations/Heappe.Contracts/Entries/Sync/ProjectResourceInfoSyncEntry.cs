namespace Heappe.Contracts.Entries.Sync;

/// <summary>
/// Represents detailed information about a resource assigned to a project, including identification, descriptive data,
/// assignment period, and resource details.
/// </summary>
/// <param name="Id">The unique identifier for the project resource entry.</param>
/// <param name="Name">The full name of the project resource.</param>
/// <param name="ShortName">A short or abbreviated name for the project resource.</param>
/// <param name="Description">An optional description providing additional details about the project resource. Can be null if no description is
/// available.</param>
/// <param name="StartDate">The date when the resource assignment to the project begins.</param>
/// <param name="EndDate">The date when the resource assignment to the project ends.</param>
/// <param name="Resource">The resource information associated with this project entry.</param>
public record ProjectResourceInfoSyncEntry
(
    Guid Id,
    string Name,
    string ShortName,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    ResourceInfoSyncEntry Resource
);