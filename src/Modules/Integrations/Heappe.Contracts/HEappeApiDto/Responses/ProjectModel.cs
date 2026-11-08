using Heappe.Contracts.CommandTemplate;

namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ProjectModel
(
    long Id,
    string Name,
    string Description,
    string AccountingString,
    DateTime StartDate,
    DateTime EndDate,
    int UsageType,
    bool UseAccountingStringForScheduler,
    string KeyScriptsDirectoryPath,
    bool IsOneToOneMapping,
    IList<CommandTemplateCompactModel> CommandTemplates
);