using Heappe.Persistence.Enums;

using Lukdrasil.Error;

namespace Heappe.Features.CreateProjectCommandTemplate;

internal static partial class Errors
{
    internal static Error FailedToCreateProjectCommandTemplate(
        string commandTemplateName,
        ECommandTemplateType commandTemplateType,
        Guid projectId,
        Guid resourceId) =>
        new ErrorBuilder()
            .WithDescription("Failed to create project command template with name '{0}', type '{1}', projectId '{2}', and resourceId '{3}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(commandTemplateName, commandTemplateType, projectId, resourceId);

    internal static Error FailedToGetLocationPartition(string queueName, Guid locationId) =>
        new ErrorBuilder()
            .WithDescription("Failed to get location partition for queue '{0}' and locationId '{1}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(queueName, locationId);

    internal static Error FailedToUpdateCommandTemplateState(string commandTemplateName, ECommandTemplateType commandTemplateType, long heappeCommandTemplateId, Guid projectId, Guid resourceId) =>
        new ErrorBuilder()
            .WithDescription("Failed to update command template state for partition with name '{0}', type '{1}', HEAppE command templateId '{2}', projectId '{3}', and resourceId '{4}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(commandTemplateName, commandTemplateType, heappeCommandTemplateId, projectId, resourceId);

    internal static Error InvalidLocation() =>
        new ErrorBuilder()
            .WithDescription("Invalid location name.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error ResourceLocationNotFound() =>
        new ErrorBuilder()
            .WithDescription("Resource location not found.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error PartitionNotFound() =>
        new ErrorBuilder()
            .WithDescription("Partition not found.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error NodeTypeForQueueNotFound() =>
        new ErrorBuilder()
            .WithDescription("Node type for queue not found.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error MissingTemplateDefinitionAndCreateModel() =>
        new ErrorBuilder()
            .WithDescription("Either TemplateDefinition or CreateModel must be provided.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error MissingHeappeCommandTemplates() =>
        new ErrorBuilder()
            .WithDescription("HEAppE instance do not have available command templates.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();
}