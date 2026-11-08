namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateModel(
    long Id,
    string Name,
    string? Description,
    string? ExtendedAllocationCommand,
    string ExecutableFile,
    string? PreparationScript,
    string CommandParameters,
    bool IsGeneric,
    bool IsEnabled,
    long ProjectId,
    long ClusterNodeTypeId,
    long? CreatedFromGenericTemplateId,
    IEnumerable<CommandTemplateParameterModel> TemplateParameters
);