namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateParameterModel(
    long Id,
    string Identifier,
    string? Description,
    string? Query
);