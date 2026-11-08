using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

/// <summary>
/// Proactively warms the per-HEAppE-URL cluster cache for a user's projects so that a subsequent
/// TemplatesInfo request is served from cache. Invoked (fire-and-forget) when the portal loads user info.
/// </summary>
public interface IClusterInfoCacheWarmer
{
    /// <summary>
    /// Schedules a background warm-up of the cluster cache for the given projects. Returns immediately;
    /// failures are logged and never propagated to the caller.
    /// </summary>
    void WarmInBackground(string subjectClaim, IReadOnlyCollection<ProjectShortName> projects, string bearerToken);
}