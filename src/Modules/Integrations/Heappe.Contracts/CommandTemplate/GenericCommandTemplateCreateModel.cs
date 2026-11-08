namespace Heappe.Contracts.CommandTemplate;

public sealed record GenericCommandTemplateCreateModel
(
    string Name,
    string? Description,
    string? ExtendedAllocationCommand,
    string? PreparationScript,
    long ProjectId,
    long ClusterNodeTypeId
);