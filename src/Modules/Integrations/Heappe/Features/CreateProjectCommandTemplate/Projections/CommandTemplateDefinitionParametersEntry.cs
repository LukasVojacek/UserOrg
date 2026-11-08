namespace Heappe.Features.CreateProjectCommandTemplate.Projections;

/// <summary>
/// Represents a parameter entry in a command template definition.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Description">The parameter description.</param>
internal sealed record CommandTemplateDefinitionParametersEntry(
    string Name,
    string Description
);