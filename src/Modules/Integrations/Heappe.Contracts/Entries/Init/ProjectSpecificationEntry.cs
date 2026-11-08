using Heappe.Contracts.HEappeApiDto.Requests;

namespace Heappe.Contracts.Entries.Init;

public sealed record ProjectSpecificationEntry
(
    string Name,
    string AccountingString,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    EUsageType UsageType,
    bool UseAccountingStringForScheduler,
    string PIEmail,
    bool IsOneToOneMapping,
    int PreferredAuthType
);