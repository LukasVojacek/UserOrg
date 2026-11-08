namespace Heappe.Features.CommandTemplatePermissions.Response;

/// <summary>
/// Represents a set of permissions and related metadata for a project queue within a cluster environment.
/// </summary>
/// <param name="QueueName">The name of the queue to which the permissions apply. Cannot be null or empty.</param>
/// <param name="ProjectResource">The identifier of the project resource associated with the permissions. Cannot be null or empty.</param>
/// <param name="ClusterName">The name of the cluster where the queue resides. Cannot be null or empty.</param>
/// <param name="ProjectShortName">The short name or abbreviation of the project. Cannot be null or empty.</param>
/// <param name="CommandTemplates">A collection of command templates that define the allowed operations for the queue. Cannot be null.</param>
internal sealed record PermissionModel(
        string QueueName,
        string ProjectResource,
        string ClusterName,
        string ProjectShortName,
        IEnumerable<CommandTemplateModel> CommandTemplates
);