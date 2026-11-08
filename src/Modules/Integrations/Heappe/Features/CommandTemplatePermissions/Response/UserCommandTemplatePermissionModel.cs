namespace Heappe.Features.CommandTemplatePermissions.Response;

/// <summary>
/// Represents a user's set of permissions for a specific command template.
/// </summary>
/// <param name="HeappeIdentifikator">The unique identifier of the command template to which the permissions apply. Cannot be null.</param>
/// <param name="Permissions">The collection of permissions assigned to the user for the specified command template. Cannot be null.</param>
internal sealed record UserCommandTemplatePermissionModel(
    string HeappeIdentifikator,
    IEnumerable<PermissionModel> Permissions
);