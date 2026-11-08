namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record UpdateProjectRequest
(
    long Id,
    string Name,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    EUsageType UsageType,
    bool UseAccountingStringForScheduler,
    bool IsOneToOneMapping
);