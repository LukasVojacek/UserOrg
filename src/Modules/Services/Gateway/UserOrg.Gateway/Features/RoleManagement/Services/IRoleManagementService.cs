using Lukdrasil.Error;
using Lukdrasil.Result;

namespace UserOrg.Gateway.Features.RoleManagement.Services;

/// <summary>
/// Service interface for role management operations in the Gateway.
/// Coordinates in-process calls to Forge.Core services.
/// </summary>
internal interface IRoleManagementService
{
    /// <summary>
    /// Retrieves all roles available in the system.
    /// </summary>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Collection of role summaries or error.</returns>
    Task<Result<IReadOnlyCollection<RoleSummary>, Error>> GetRolesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves detailed information about a specific role.
    /// </summary>
    /// <param name="roleName">Name of the role to retrieve.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Role details including permissions or error.</returns>
    Task<Result<RoleDetail, Error>> GetRoleAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new role with the specified details.
    /// </summary>
    /// <param name="request">Request containing role name, description, and permissions.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Created role details or error.</returns>
    Task<Result<RoleDetail, Error>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the active/inactive status of a role.
    /// </summary>
    /// <param name="roleName">Name of the role to update.</param>
    /// <param name="isActive">New active status.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> UpdateRoleStatusAsync(string roleName, bool isActive, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the permissions assigned to a role.
    /// </summary>
    /// <param name="roleName">Name of the role to update.</param>
    /// <param name="permissions">Collection of permission names to assign.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> UpdateRolePermissionsAsync(string roleName, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the description of a role.
    /// </summary>
    /// <param name="roleName">Name of the role to update.</param>
    /// <param name="description">New description text.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> UpdateRoleDescriptionAsync(string roleName, string? description, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the level of a role.
    /// </summary>
    /// <param name="roleName">Name of the role to update.</param>
    /// <param name="level">New level value.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> UpdateRoleLevelAsync(string roleName, int level, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the category of a role.
    /// </summary>
    /// <param name="roleName">Name of the role to update.</param>
    /// <param name="category">New category value.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> UpdateRoleCategoryAsync(string roleName, string category, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all available permissions in the system.
    /// </summary>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Collection of permission information or error.</returns>
    Task<Result<IReadOnlyCollection<PermissionInfo>, Error>> GetPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a specific permission by its key.
    /// </summary>
    /// <param name="permissionKey">The permission key (e.g., "perm:users:read").</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Permission information or error.</returns>
    Task<Result<PermissionInfo, Error>> GetPermissionAsync(string permissionKey, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new permission.
    /// </summary>
    /// <param name="permissionKey">The permission key (e.g., "perm:users:read").</param>
    /// <param name="description">Description of what this permission allows.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Created permission information or error.</returns>
    Task<Result<PermissionInfo, Error>> CreatePermissionAsync(string permissionKey, string description, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a permission.
    /// </summary>
    /// <param name="permissionKey">The permission key to delete.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> DeletePermissionAsync(string permissionKey, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a permission is assigned to any roles.
    /// </summary>
    /// <param name="permissionKey">The permission key to check.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>True if assigned to roles, false otherwise, or error.</returns>
    Task<Result<bool, Error>> IsPermissionAssignedAsync(string permissionKey, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all endpoint permission mappings.
    /// </summary>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Collection of endpoint mappings or error.</returns>
    Task<Result<IReadOnlyCollection<EndpointMappingInfo>, Error>> GetEndpointMappingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets permissions for a specific endpoint.
    /// </summary>
    /// <param name="endpointName">Name of the endpoint to configure.</param>
    /// <param name="permissions">Collection of permission names to associate.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> SetEndpointPermissionsAsync(string endpointName, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new endpoint mapping.
    /// </summary>
    /// <param name="endpointName">Unique name for the endpoint.</param>
    /// <param name="pathTemplate">Path template (e.g., "/api/users/{id}").</param>
    /// <param name="httpMethod">HTTP method (GET, POST, etc.).</param>
    /// <param name="description">Optional description.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Created endpoint mapping or error.</returns>
    Task<Result<EndpointMappingInfo, Error>> CreateEndpointMappingAsync(string endpointName, string pathTemplate, string httpMethod, string? description, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an endpoint mapping.
    /// </summary>
    /// <param name="endpointName">Name of the endpoint to delete.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> DeleteEndpointMappingAsync(string endpointName, CancellationToken cancellationToken);

    /// <summary>
    /// Exports all roles and their permissions to a JSON stream.
    /// </summary>
    /// <param name="outputStream">Stream to write the export data to.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Success indicator or error.</returns>
    Task<Result<bool, Error>> ExportRolesAsync(Stream outputStream, CancellationToken cancellationToken);

    /// <summary>
    /// Imports roles and their permissions from a JSON stream.
    /// </summary>
    /// <param name="inputStream">Stream containing the import data.</param>
    /// <param name="overwriteExisting">If true, overwrites existing roles.</param>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    /// <returns>Import summary with statistics or error.</returns>
    Task<Result<ImportExportSummary, Error>> ImportRolesAsync(Stream inputStream, bool overwriteExisting, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates the endpoint-permission snapshot cache so the running Gateway picks up
    /// out-of-band changes (e.g. a startup reconciliation import) without a rebuild.
    /// </summary>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    Task InvalidateEndpointPermissionCacheAsync(CancellationToken cancellationToken);
}