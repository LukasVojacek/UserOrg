namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateCreateModel
(
    string Name,
    string? Description,
    string? ExtendedAllocationCommand,
    string ExecutableFile,
    string? PreparationScript,
    long ProjectId,
    long ClusterNodeTypeId,
    IEnumerable<CommandTemplateParameterCreateModel> TemplateParameters,
    string? SessionCode
);