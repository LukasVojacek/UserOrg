namespace UserOrg.Gateway;

/// <summary>
/// Gateway-side audit action catalog. Kept separate from Core's own catalog - the two projects
/// share no reference and the action sets are disjoint by design.
/// </summary>
internal static class GatewayAuditActions
{
    internal const string AuthorizationDenied = "gateway.authorization.denied";

    internal const string AuthorizationBypassed = "gateway.authorization.bypassed";

    internal const string RoleManagementSignedIn = "gateway.role_management.signed_in";

    internal const string RoleManagementSignedOut = "gateway.role_management.signed_out";

    internal const string RbacRoleCreated = "gateway.rbac_role.created";

    internal const string RbacRoleUpdated = "gateway.rbac_role.updated";

    internal const string RbacPermissionCreated = "gateway.rbac_permission.created";

    internal const string RbacPermissionDeleted = "gateway.rbac_permission.deleted";

    internal const string EndpointMappingCreated = "gateway.endpoint_mapping.created";

    internal const string EndpointMappingUpdated = "gateway.endpoint_mapping.updated";

    internal const string EndpointMappingDeleted = "gateway.endpoint_mapping.deleted";

    internal const string RbacImported = "gateway.rbac.imported";

    internal const string RbacExported = "gateway.rbac.exported";

    internal const string RbacSeeded = "gateway.rbac.seeded";

    internal const string SigningKeyGenerated = "gateway.signing_key.generated";

    internal const string SigningKeyRotated = "gateway.signing_key.rotated";

    internal const string ApiKeyAuthenticated = "gateway.api_key.authenticated";

    internal const string DevOverrideAuthenticated = "gateway.dev_override.authenticated";

    /// <summary>
    /// Discriminates the causes that share the <see cref="AuthorizationDenied"/> action, so an
    /// endpoint-map outage is not read back from the ledger as a policy denial storm. Each reason is
    /// area-qualified exactly like Core's, so one query over the whole ledger can tell a gateway
    /// denial from a domain one without knowing which side wrote the record.
    /// </summary>
    internal static class DenialReasons
    {
        internal const string NoPermissionMapping = "gateway.no_permission_mapping";

        internal const string PermissionLookupFailed = "gateway.permission_lookup_failed";

        internal const string RequiredDdiPermission = "gateway.required_ddi_permission";

        internal const string RequiredProviderPermission = "gateway.required_provider_permission";

        internal const string RequiredServicePermission = "gateway.required_service_permission";

        internal const string InsufficientPermissions = "gateway.insufficient_permissions";

        internal const string InsufficientProjectPermissions = "gateway.insufficient_project_permissions";

        internal const string PermissionServiceMissing = "gateway.permission_service_missing";

        internal const string RoleManagementSignInFailed = "gateway.role_management_sign_in_failed";

        internal const string ApiKeyMissing = "gateway.api_key_missing";

        internal const string ApiKeyLookupTimedOut = "gateway.api_key_lookup_timed_out";

        internal const string ApiKeyInvalid = "gateway.api_key_invalid";
    }
}