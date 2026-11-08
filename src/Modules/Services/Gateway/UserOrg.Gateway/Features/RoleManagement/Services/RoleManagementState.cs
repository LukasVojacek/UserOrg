using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Components;

namespace UserOrg.Gateway.Features.RoleManagement.Services;

/// <summary>
/// Centralized state management for RoleManagement feature (scoped per Blazor Server circuit).
/// Provides data caching, change notifications, and CRUD operations delegating to <see cref="IRoleManagementService"/>.
/// </summary>
internal sealed class RoleManagementState : IDisposable
{
    private readonly IRoleManagementService _service;
    private readonly ILogger<RoleManagementState> _logger;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Exposes the underlying service for direct access when needed.
    /// </summary>
    public IRoleManagementService Service => _service;

    /// <summary>
    /// Event raised when state changes (triggers component re-rendering).
    /// </summary>
    public event Action? OnChange;

    // Data - persisted across circuit pause/resume
    // Note: Properties must be public for [PersistentState] to work
    [PersistentState]
    public IReadOnlyList<RoleSummary> Roles { get; set; } = Array.Empty<RoleSummary>();

    [PersistentState]
    public IReadOnlyList<PermissionInfo> Permissions { get; set; } = Array.Empty<PermissionInfo>();

    [PersistentState]
    public IReadOnlyList<EndpointMappingInfo> EndpointMappings { get; set; } = Array.Empty<EndpointMappingInfo>();

    // UI State - persisted to retain user feedback messages
    public bool IsLoading { get; private set; }
    public bool IsBusy { get; private set; }

    [PersistentState]
    public string? ErrorMessage { get; set; }

    [PersistentState]
    public string? SuccessMessage { get; set; }

    public RoleManagementState(
        IRoleManagementService service,
        ILogger<RoleManagementState> logger)
    {
        _service = service;
        _logger = logger;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Loads roles, permissions, and endpoint mappings from the service.
    /// </summary>
    public async Task LoadReferenceDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        // Don't clear SuccessMessage here - it may contain important feedback from CRUD operations
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;

            var rolesTask = _service.GetRolesAsync(ct);
            var permissionsTask = _service.GetPermissionsAsync(ct);
            var endpointsTask = _service.GetEndpointMappingsAsync(ct);

            await Task.WhenAll(rolesTask, permissionsTask, endpointsTask);

            var rolesResult = await rolesTask;
            var permissionsResult = await permissionsTask;
            var endpointsResult = await endpointsTask;

            if (rolesResult.IsSuccess)
            {
                Roles = rolesResult.Value.ToList();
            }
            else
            {
                ErrorMessage = rolesResult.Error.Description ?? "Failed to load roles.";
                _logger.LogError("Failed to load roles: {ErrorDescription}", rolesResult.Error.Description);
            }

            if (permissionsResult.IsSuccess)
            {
                Permissions = permissionsResult.Value.ToList();
            }
            else
            {
                ErrorMessage ??= permissionsResult.Error.Description ?? "Failed to load permissions.";
                _logger.LogError("Failed to load permissions: {ErrorDescription}", permissionsResult.Error.Description);
            }

            if (endpointsResult.IsSuccess)
            {
                EndpointMappings = endpointsResult.Value.ToList();
            }
            else
            {
                ErrorMessage ??= endpointsResult.Error.Description ?? "Failed to load endpoint mappings.";
                _logger.LogError("Failed to load endpoint mappings: {ErrorDescription}", endpointsResult.Error.Description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while loading RoleManagement reference data");
            ErrorMessage = "Failed to load data. Please refresh the page.";
        }
        finally
        {
            IsLoading = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Creates a new role with the given request data.
    /// </summary>
    public async Task<Result<RoleDetail, Error>> CreateRoleAsync(CreateRoleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.CreateRoleAsync(request, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Role '{request.Name}' created successfully.";
                _logger.LogInformation("Role {RoleName} created successfully.", request.Name);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to create role.";
                _logger.LogError("Failed to create role {RoleName}: {ErrorDescription}", request.Name, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates permissions for the specified role.
    /// </summary>
    public async Task<Result<bool, Error>> UpdateRolePermissionsAsync(string roleName, IReadOnlyCollection<string> permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentNullException.ThrowIfNull(permissions);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.UpdateRolePermissionsAsync(roleName, permissions, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Permissions for role '{roleName}' updated successfully.";
                _logger.LogInformation("Role {RoleName} permissions updated.", roleName);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to update role permissions.";
                _logger.LogError("Failed to update permissions for role {RoleName}: {ErrorDescription}", roleName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates description for the specified role.
    /// </summary>
    public async Task<Result<bool, Error>> UpdateRoleDescriptionAsync(string roleName, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.UpdateRoleDescriptionAsync(roleName, description, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Description for role '{roleName}' updated successfully.";
                _logger.LogInformation("Role {RoleName} description updated.", roleName);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to update role description.";
                _logger.LogError("Failed to update description for role {RoleName}: {ErrorDescription}", roleName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates level for the specified role.
    /// </summary>
    public async Task<Result<bool, Error>> UpdateRoleLevelAsync(string roleName, int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.UpdateRoleLevelAsync(roleName, level, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Level for role '{roleName}' updated successfully.";
                _logger.LogInformation("Role {RoleName} level updated to {Level}.", roleName, level);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to update role level.";
                _logger.LogError("Failed to update level for role {RoleName}: {ErrorDescription}", roleName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates category for the specified role.
    /// </summary>
    public async Task<Result<bool, Error>> UpdateRoleCategoryAsync(string roleName, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.UpdateRoleCategoryAsync(roleName, category, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Category for role '{roleName}' updated successfully.";
                _logger.LogInformation("Role {RoleName} category updated to {Category}.", roleName, category);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to update role category.";
                _logger.LogError("Failed to update category for role {RoleName}: {ErrorDescription}", roleName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Updates the active/inactive status of a role.
    /// </summary>
    public async Task<Result<bool, Error>> UpdateRoleStatusAsync(string roleName, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.UpdateRoleStatusAsync(roleName, isActive, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Role '{roleName}' {(isActive ? "activated" : "deactivated")} successfully.";
                _logger.LogInformation("Role {RoleName} status updated to {IsActive}.", roleName, isActive);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to update role status.";
                _logger.LogError("Failed to update status for role {RoleName}: {ErrorDescription}", roleName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Sets permissions for the specified endpoint.
    /// </summary>
    public async Task<Result<bool, Error>> SetEndpointPermissionsAsync(string endpointName, IReadOnlyCollection<string> permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        ArgumentNullException.ThrowIfNull(permissions);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.SetEndpointPermissionsAsync(endpointName, permissions, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Permissions for endpoint '{endpointName}' updated successfully.";
                _logger.LogInformation("Endpoint {EndpointName} permissions updated.", endpointName);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to update endpoint permissions.";
                _logger.LogError("Failed to update permissions for endpoint {EndpointName}: {ErrorDescription}", endpointName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Clears success/error messages.
    /// </summary>
    public void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Creates a new permission.
    /// </summary>
    public async Task<Result<PermissionInfo, Error>> CreatePermissionAsync(string permissionKey, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.CreatePermissionAsync(permissionKey, description, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Permission '{permissionKey}' created successfully.";
                _logger.LogInformation("Permission {PermissionKey} created.", permissionKey);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to create permission.";
                _logger.LogError("Failed to create permission {PermissionKey}: {ErrorDescription}", permissionKey, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// Deletes a permission.
    /// </summary>
    public async Task<Result<bool, Error>> DeletePermissionAsync(string permissionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionKey);

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        NotifyStateChanged();

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            var result = await _service.DeletePermissionAsync(permissionKey, ct);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Permission '{permissionKey}' deleted successfully.";
                _logger.LogInformation("Permission {PermissionKey} deleted.", permissionKey);

                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to delete permission.";
                _logger.LogError("Failed to delete permission {PermissionKey}: {ErrorDescription}", permissionKey, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public async Task<Result<EndpointMappingInfo, Error>> CreateEndpointAsync(
        string endpointName,
        string pathTemplate,
        string httpMethod,
        string? description)
    {
        if (IsBusy)
        {
            return Result<EndpointMappingInfo, Error>.Failure(
                new Error("Busy", Lukdrasil.Error.ErrorSeverity.Error, "Another operation is in progress."));
        }

        IsBusy = true;
        NotifyStateChanged();

        try
        {
            var result = await _service.CreateEndpointMappingAsync(endpointName, pathTemplate, httpMethod, description, _cts.Token);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Endpoint '{endpointName}' created successfully.";
                _logger.LogInformation("Endpoint {EndpointName} created", endpointName);
                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to create endpoint.";
                _logger.LogError("Failed to create endpoint {EndpointName}: {ErrorDescription}", endpointName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    public async Task<Result<bool, Error>> DeleteEndpointAsync(string endpointName)
    {
        if (IsBusy)
        {
            return Result<bool, Error>.Failure(
                new Error("Busy", Lukdrasil.Error.ErrorSeverity.Error, "Another operation is in progress."));
        }

        IsBusy = true;
        NotifyStateChanged();

        try
        {
            var result = await _service.DeleteEndpointMappingAsync(endpointName, _cts.Token);

            if (result.IsSuccess)
            {
                SuccessMessage = $"Endpoint '{endpointName}' deleted successfully.";
                _logger.LogInformation("Endpoint {EndpointName} deleted", endpointName);
                await LoadReferenceDataAsync();
            }
            else
            {
                ErrorMessage = result.Error.Description ?? "Failed to delete endpoint.";
                _logger.LogError("Failed to delete endpoint {EndpointName}: {ErrorDescription}", endpointName, result.Error.Description);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Suppress cancellation exceptions during disposal
        }

        _cts?.Dispose();
        _cts = null;
    }
}