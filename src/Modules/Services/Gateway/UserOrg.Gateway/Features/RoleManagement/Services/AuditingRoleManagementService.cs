using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

namespace UserOrg.Gateway.Features.RoleManagement.Services;

internal sealed class AuditingRoleManagementService(IRoleManagementService inner, IAuditLogger<AuditingRoleManagementService> auditLogger, RoleManagementAuditActor actor) : IRoleManagementService
{
    private const string RoleTarget = "rbac_role";

    private const string PermissionTarget = "rbac_permission";

    private const string EndpointMappingTarget = "endpoint_mapping";

    public Task<Result<IReadOnlyCollection<RoleSummary>, Error>> GetRolesAsync(CancellationToken cancellationToken)
        => inner.GetRolesAsync(cancellationToken);

    public Task<Result<RoleDetail, Error>> GetRoleAsync(string roleName, CancellationToken cancellationToken)
        => inner.GetRoleAsync(roleName, cancellationToken);

    public Task<Result<RoleDetail, Error>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacRoleCreated, RoleTarget, request.Name, () => inner.CreateRoleAsync(request, cancellationToken));

    public Task<Result<bool, Error>> UpdateRoleStatusAsync(string roleName, bool isActive, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacRoleUpdated, RoleTarget, roleName, () => inner.UpdateRoleStatusAsync(roleName, isActive, cancellationToken));

    public Task<Result<bool, Error>> UpdateRolePermissionsAsync(string roleName, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacRoleUpdated, RoleTarget, roleName, () => inner.UpdateRolePermissionsAsync(roleName, permissions, cancellationToken));

    public Task<Result<bool, Error>> UpdateRoleDescriptionAsync(string roleName, string? description, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacRoleUpdated, RoleTarget, roleName, () => inner.UpdateRoleDescriptionAsync(roleName, description, cancellationToken));

    public Task<Result<bool, Error>> UpdateRoleLevelAsync(string roleName, int level, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacRoleUpdated, RoleTarget, roleName, () => inner.UpdateRoleLevelAsync(roleName, level, cancellationToken));

    public Task<Result<bool, Error>> UpdateRoleCategoryAsync(string roleName, string category, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacRoleUpdated, RoleTarget, roleName, () => inner.UpdateRoleCategoryAsync(roleName, category, cancellationToken));

    public Task<Result<IReadOnlyCollection<PermissionInfo>, Error>> GetPermissionsAsync(CancellationToken cancellationToken)
        => inner.GetPermissionsAsync(cancellationToken);

    public Task<Result<PermissionInfo, Error>> GetPermissionAsync(string permissionKey, CancellationToken cancellationToken)
        => inner.GetPermissionAsync(permissionKey, cancellationToken);

    public Task<Result<PermissionInfo, Error>> CreatePermissionAsync(string permissionKey, string description, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacPermissionCreated, PermissionTarget, permissionKey, () => inner.CreatePermissionAsync(permissionKey, description, cancellationToken));

    public Task<Result<bool, Error>> DeletePermissionAsync(string permissionKey, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacPermissionDeleted, PermissionTarget, permissionKey, () => inner.DeletePermissionAsync(permissionKey, cancellationToken));

    public Task<Result<bool, Error>> IsPermissionAssignedAsync(string permissionKey, CancellationToken cancellationToken)
        => inner.IsPermissionAssignedAsync(permissionKey, cancellationToken);

    public Task<Result<IReadOnlyCollection<EndpointMappingInfo>, Error>> GetEndpointMappingsAsync(CancellationToken cancellationToken)
        => inner.GetEndpointMappingsAsync(cancellationToken);

    public Task<Result<bool, Error>> SetEndpointPermissionsAsync(string endpointName, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.EndpointMappingUpdated, EndpointMappingTarget, endpointName, () => inner.SetEndpointPermissionsAsync(endpointName, permissions, cancellationToken));

    public Task<Result<EndpointMappingInfo, Error>> CreateEndpointMappingAsync(string endpointName, string pathTemplate, string httpMethod, string? description, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.EndpointMappingCreated, EndpointMappingTarget, endpointName, () => inner.CreateEndpointMappingAsync(endpointName, pathTemplate, httpMethod, description, cancellationToken));

    public Task<Result<bool, Error>> DeleteEndpointMappingAsync(string endpointName, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.EndpointMappingDeleted, EndpointMappingTarget, endpointName, () => inner.DeleteEndpointMappingAsync(endpointName, cancellationToken));

    public Task<Result<bool, Error>> ExportRolesAsync(Stream outputStream, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacExported, targetType: null, targetId: null, () => inner.ExportRolesAsync(outputStream, cancellationToken));

    public Task<Result<ImportExportSummary, Error>> ImportRolesAsync(Stream inputStream, bool overwriteExisting, CancellationToken cancellationToken)
        => AuditedAsync(GatewayAuditActions.RbacImported, targetType: null, targetId: null, () => inner.ImportRolesAsync(inputStream, overwriteExisting, cancellationToken));

    public Task InvalidateEndpointPermissionCacheAsync(CancellationToken cancellationToken)
        => inner.InvalidateEndpointPermissionCacheAsync(cancellationToken);

    private async Task<Result<T, Error>> AuditedAsync<T>(string action, string? targetType, string? targetId, Func<Task<Result<T, Error>>> mutation)
    {
        var acting = await actor.ResolveActorAsync();
        var result = await mutation();
        if (result.IsSuccess)
        {
            await auditLogger.AuditAsync(AuditEvent.Success(action, acting.ActorId, acting.ActorType) with
            {
                TargetType = targetType,
                TargetId = targetId,
                Data = SessionData(acting),
            });
        }

        return result;
    }

    private static Dictionary<string, object?>? SessionData(AuditActor acting)
        => acting.SessionId is null ? null : new() { [Constants.RoleManagementAccessIdClaim] = acting.SessionId };
}