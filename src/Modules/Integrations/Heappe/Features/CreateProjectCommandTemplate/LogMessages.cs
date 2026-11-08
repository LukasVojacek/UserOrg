using Microsoft.Extensions.Logging;

namespace Heappe.Features.CreateProjectCommandTemplate;

/// <summary>
/// Provides structured logging methods for command template creation flow.
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Creating command template: Heappe={ShouldCreateHeappe}, Local={ShouldCreateLocal}")]
    internal static partial void LogCreatingCommandTemplate(this ILogger logger, bool shouldCreateHeappe, bool shouldCreateLocal);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to create Heappe template '{TemplateName}'")]
    internal static partial void LogFailedToCreateHeappeTemplate(this ILogger logger, string templateName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to create local template '{TemplateName}'")]
    internal static partial void LogFailedToCreateLocalTemplate(this ILogger logger, string templateName);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to retrieve LocationPartition for queue '{QueueName}' at location '{LocationId}' while creating template '{TemplateName}'")]
    internal static partial void LogLocationPartitionRetrievalFailure(this ILogger logger, string templateName, string queueName, Guid locationId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Failed to create project command template '{TemplateName}' for queue '{QueueName}' at location '{LocationId}'")]
    internal static partial void LogProjectCommandTemplateCreationError(this ILogger logger, string templateName, string queueName, Guid locationId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Failed to create HEAppE command template '{TemplateName}' in HEAppE project '{ProjectName}' for queue '{QueueName}'")]
    internal static partial void LogHeappeCommandTemplateCreationError(this ILogger logger, string templateName, string projectName, string queueName);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Failed to update command template state for template '{TemplateName}', HEAppE ID '{HeappeCommandTemplateId}', queue '{QueueName}', location '{LocationId}'")]
    internal static partial void LogCommandTemplateStateUpdateFailure(this ILogger logger, string templateName, long heappeCommandTemplateId, string queueName, Guid locationId);

}