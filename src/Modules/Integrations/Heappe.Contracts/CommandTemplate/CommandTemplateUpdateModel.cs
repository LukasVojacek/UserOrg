namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateUpdateModel
(
    long Id,
    string Name,
    string? Description,
    string? ExtendedAllocationCommand,
    string ExecutableFile,
    string? PreparationScript,
    long ClusterNodeTypeId,
    bool IsEnabled,
    string? SessionCode
);