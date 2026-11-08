namespace UserOrganizationManagement.API.Middleware;

/// <summary>
/// The API host's own audit action catalog. Kept separate from Core's catalog because the host cannot
/// see Core's internals.
/// </summary>
internal static class HostAuditActions
{
    internal const string FeatureFlagRequestBlocked = "feature_flag.request.blocked";
}