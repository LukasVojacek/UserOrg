namespace UserOrg.Gateway.Configuration;

/// <summary>
/// Configuration for gateway-level authorization behaviour.
/// </summary>
internal sealed class GatewayAuthorizationOptions
{
    public const string SectionName = "GatewayAuthorization";

    /// <summary>
    /// When <see langword="false"/> (default), system roles (Platform Administrator, Provider Administrator)
    /// skip the gateway's project-membership check for project-scoped endpoints — fine-grained project scoping
    /// is left to the API. Set to <see langword="true"/> to also enforce project membership for system roles
    /// at the gateway.
    /// Absent configuration keeps the default (bypass enabled).
    /// </summary>
    public bool EnforceProjectMembershipForSystemRoles { get; set; }
}