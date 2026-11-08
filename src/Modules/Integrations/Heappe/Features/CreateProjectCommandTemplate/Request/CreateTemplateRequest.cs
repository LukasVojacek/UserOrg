namespace Heappe.Features.CreateProjectCommandTemplate.Request;

/// <summary>
/// Represents a command template parameter for request serialization.
/// Must have exactly these two properties: Name and Description.
/// </summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Description">The parameter description.</param>
public sealed record CreateCommandTemplateParameterDto(
    string Name,
    string Description);

/// <summary>
/// Represents the request body for creating a command template.
/// </summary>
/// <param name="ProjectIdentifier">The target project and resource.</param>
/// <param name="PartitionName">The HEAppE partition name.</param>
/// <param name="LocationName">The location name.</param>
/// <param name="Name">The command template name.</param>
/// <param name="Description">The command template description.</param>
/// <param name="PreparationScriptPath">Optional preparation command script.</param>
/// <param name="ExecutionScriptPath">Optional execution command script.</param>
/// <param name="CommandTemplateParameters">List of command template parameters.</param>
public sealed record CreateTemplateRequest(
    ProjectResourceTarget ProjectIdentifier,
    string PartitionName,
    string LocationName,
    string? AggregationName,
    string Name,
    string? Description,
    string? PreparationScriptPath,
    string? ExecutionScriptPath,
    IList<CreateCommandTemplateParameterDto> CommandTemplateParameters
);