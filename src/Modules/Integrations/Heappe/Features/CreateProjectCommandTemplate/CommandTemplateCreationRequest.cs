using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.CreateProjectCommandTemplate.Projections;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

namespace Heappe.Features.CreateProjectCommandTemplate;

/// <summary>
/// DTO encapsulating all required data for command template creation.
/// Automatically determines what to create based on null checks:
/// creates Heappe if <see cref="ExistingHeappeTemplate"/> is null,
/// local if <see cref="ExistingProjectTemplate"/> is null, or syncs state if both exist.
/// Only instantiate via factory methods: <see cref="WithDefinition"/>, or <see cref="WithCreateModel"/>.
/// </summary>
internal record CommandTemplateCreationRequest
{
    // Template data - EXACTLY ONE of these is set (other is null)
    internal required CommandTemplateDefinitionEntry? TemplateDefinition { get; init; }
    internal required CommandTemplateCreateModel? CreateModel { get; init; }

    internal required BaseHeappeConnectionInfo ServiceConnectionInfo { get; init; }
    internal required ProjectModel HeappeProject { get; init; }
    internal required NodeTypeModel NodeType { get; init; }

    internal required Guid LocationId { get; init; }
    internal required Guid ProjectId { get; init; }
    internal required Guid ResourceId { get; init; }

    internal required ProjectCommandTemplate? ExistingProjectTemplate { get; init; }
    internal required CommandTemplateCompactModel? ExistingHeappeTemplate { get; init; }
    internal required ECommandTemplateState DefaultCommandTemplateState { get; init; }

    /// <summary>
    /// Factory method to create request with <see cref="CommandTemplateDefinitionEntry"/>.
    /// Used by the sync service to provide template definitions with pre-constructed complete paths.
    /// </summary>
    internal static CommandTemplateCreationRequest WithDefinition(
        CommandTemplateDefinitionEntry syncDefinition,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        Guid locationId,
        Guid projectId,
        Guid resourceId,
        ProjectCommandTemplate? existingProjectTemplate = null,
        CommandTemplateCompactModel? existingHeappeTemplate = null)
    {

        return new()
        {
            TemplateDefinition = syncDefinition,
            CreateModel = null,
            ServiceConnectionInfo = serviceConnectionInfo,
            HeappeProject = heappeProject,
            NodeType = nodeType,
            LocationId = locationId,
            ProjectId = projectId,
            ResourceId = resourceId,
            ExistingProjectTemplate = existingProjectTemplate,
            ExistingHeappeTemplate = existingHeappeTemplate,
            DefaultCommandTemplateState = ECommandTemplateState.Active
        };
    }

    /// <summary>Factory method to create request with <see cref="CommandTemplateCreateModel"/>.</summary>
    internal static CommandTemplateCreationRequest WithCreateModel(
        CommandTemplateCreateModel createModel,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        Guid locationId,
        Guid projectId,
        Guid resourceId,
        ProjectCommandTemplate? existingProjectTemplate = null,
        CommandTemplateCompactModel? existingHeappeTemplate = null) =>
        new()
        {
            TemplateDefinition = null,
            CreateModel = createModel,
            ServiceConnectionInfo = serviceConnectionInfo,
            HeappeProject = heappeProject,
            NodeType = nodeType,
            LocationId = locationId,
            ProjectId = projectId,
            ResourceId = resourceId,
            ExistingProjectTemplate = existingProjectTemplate,
            ExistingHeappeTemplate = existingHeappeTemplate,
            DefaultCommandTemplateState = ECommandTemplateState.Inactive
        };
}