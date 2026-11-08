namespace UserOrg.Gateway;

internal static class Constants
{
    public const string BearerSchemeId = "Bearer";
    public const string ApiKeySchemeId = "ApiKey";
    public const string DynamicSchemeId = "DynamicAuthScheme";
    public const string RoleManagementCookieSchemeId = "RoleManagementCookie";
    public const string RoleManagementAccessIdClaim = "rm_access_id";
    public const string RoleManagementAccessPolicyName = "RoleManagementAccess";
    public const string PublicWithInternalTokenSchemeId = "PublicWithInternalToken";
    public const string PublicAccessClaimType = "public_access";

    // Develop-only auth override (ADR-0045).
    public const string DevOverrideSchemeId = "DevAuthOverride";
}