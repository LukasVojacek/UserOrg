namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateParameterCreateModel
(
    string Identifier,
    string Description,
    string Query
);