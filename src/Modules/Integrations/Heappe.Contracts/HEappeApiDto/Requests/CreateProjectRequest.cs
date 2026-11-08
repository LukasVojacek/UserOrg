namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record CreateProjectRequest
(
    string Name,
    string? Description,
    string AccountingString,
    DateTime StartDate,
    DateTime EndDate,
    EUsageType UsageType,
    bool UseAccountingStringForScheduler,
    string PIEmail,
    bool IsOneToOneMapping
);