namespace Heappe.Contracts.Entries;

/// <summary>
/// Represents a resource entry containing identification, descriptive information, and optional start and end dates.
/// </summary>
/// <param name="Id">The unique identifier for the resource entry.</param>
/// <param name="Name">The name of the resource. This value cannot be null.</param>
/// <param name="Description">An optional description providing additional details about the resource. Can be null if no description is available.</param>
/// <param name="StartDate">The optional start date for the resource's availability or activity. Can be null if not specified.</param>
/// <param name="EndDate">The optional end date for the resource's availability or activity. Can be null if not specified.</param>
public record ResourceInfoEntry
(
    Guid Id,
    string Name,
    string? Description,
    DateTime? StartDate,
    DateTime? EndDate
);