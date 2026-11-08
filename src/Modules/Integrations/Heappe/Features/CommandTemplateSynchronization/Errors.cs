using Heappe.Persistence.Enums;

using Lukdrasil.Error;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Heappe.Features.CommandTemplateSynchronization;

/// <summary>
/// Provides factory methods for creating standardized error messages related to project and resource management within
/// the HEAppE integration module.
/// </summary>
/// <remarks>The static methods in this class generate error objects for various failure and warning scenarios
/// encountered during command template synchronization and resource operations. Each method returns an error with a
/// descriptive message and an appropriate severity level, enabling consistent error handling and reporting throughout
/// the application. This class is intended for internal use within the HEAppE integration features.</remarks>
internal static partial class Errors
{
    internal static Error ProjectOrResourceExpiredWarning(string projectName, string resourceName) => new ErrorBuilder()
        .WithDescription("Project '{0}' or project resource '{1}' expired.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(projectName, resourceName);

    internal static Error MissingHEAppEUrlInResource(string resourceName) => new ErrorBuilder()
          .WithDescription("Project resource '{0}' does not have setup HEAppE url for synchronization.")
          .WithSeverity(ErrorSeverity.Warning)
          .Build(resourceName);

    // Reason-free by design (ADR-0077): the kill-switch reason is admin-only and may describe
    // an unpatched vulnerability, so it must never surface on this write/sync path.
    internal static Error AllLocationsFrozen(string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("All locations for project '{0}', resource '{1}' are frozen by an active kill-switch; synchronization skipped.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(projectShortName, resourceName);

    internal static Error HeappeProjectNotFound(string resourceName, HeappeIdentifier heappeIdentifier, string projectShortName) => new ErrorBuilder()
        .WithDescription("HEAppE project for resource '{0}' not found on instance '{1}' (project '{2}').")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(resourceName, heappeIdentifier, projectShortName);

    internal static Error HeappeClusterMissing(string clusterName, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("HEAppE cluster '{0}' not found in HEAppE instance '{1}' for project '{2}', resource '{3}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(clusterName, heappeIdentifier, projectShortName, resourceName);

    internal static Error HeappeClusterAggregationMissing(string clusterName, string aggregationName, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("HEAppE cluster '{0}' with aggregation '{1}' not present in HEAppE instance '{2}' for project '{3}', resource '{4}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(clusterName, aggregationName, heappeIdentifier, projectShortName, resourceName);

    internal static Error FailedToUpdateCommandTemplateState(string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to update command template state to 'InActive' for project '{0}' and resource '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, resourceName);

    internal static Error FailedToListHeappeClusters(HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to list clusters on HEAppE instance '{0}' for project '{1}', resource '{2}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeIdentifier, projectShortName, resourceName);

    internal static Error FailedToRetrieveHeappeCommandTemplates(long heappeProjectId, HeappeIdentifier heappeIdentifier, string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to retrieve command templates for HEAppE project '{0}' on instance '{1}' (project '{2}', resource '{3}').")
        .WithSeverity(ErrorSeverity.Error)
        .Build(heappeProjectId, heappeIdentifier, projectShortName, resourceName);

    internal static Error CommandTemplateSyncFailure(string commandTemplateName, HeappeIdentifier heappeIdentifier, string queueName, Guid locationId, string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to synchronize command template '{0}' with HEAppE instance '{1}' for queue '{2}' at location '{3}' (project '{4}', resource '{5}').")
        .WithSeverity(ErrorSeverity.Error)
        .Build(commandTemplateName, heappeIdentifier, queueName, locationId, projectShortName, resourceName);

    internal static Error NoCommandTemplateDefinitionsForLocation(ELocationType locationType, Guid locationId) => new ErrorBuilder()
        .WithDescription("No command template definitions found for location type '{0}', location '{1}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(locationType, locationId);

    internal static Error FailedToRetrieveCommandTemplatesForLocation(ELocationType locationType, Guid locationId) => new ErrorBuilder()
        .WithDescription("Failed to retrieve command template definitions for location type '{0}', location '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(locationType, locationId);
}