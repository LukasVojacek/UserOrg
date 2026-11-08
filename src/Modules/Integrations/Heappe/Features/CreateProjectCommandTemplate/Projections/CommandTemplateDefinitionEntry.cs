using Heappe.Persistence.Enums;

namespace Heappe.Features.CreateProjectCommandTemplate.Projections;

/// <summary>
/// Represents a read-only projection of a command template definition with its associated metadata and parameters.
/// </summary>
/// <param name="Id">Unique identifier for the command template.</param>
/// <param name="Name">Name of the command template.</param>
/// <param name="Description">Optional description of the command template's purpose.</param>
/// <param name="Type">Type categorization of the command template.</param>
/// <param name="PreparationCommand">Optional command to execute during the preparation phase.</param>
/// <param name="ExecutionCommand">Optional command to execute during the execution phase.</param>
/// <param name="LocationPartition">The location partition associated with the command template.</param>
/// <param name="Parameters">Collection of parameter definitions required by the command template.</param>
internal sealed record CommandTemplateDefinitionEntry(
    Guid Id,
    string Name,
    string? Description,
    ECommandTemplateType Type,
    string? PreparationCommand,
    string ExecutionCommand,
    LocationPartitionEntry? LocationPartition,
    IList<CommandTemplateDefinitionParametersEntry> Parameters
);