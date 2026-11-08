namespace Heappe.Helpers;

/// <summary>
/// Matches a HEAppE cluster's <c>MasterNodeName</c> against a Location's login endpoint.
/// The same login host is stored differently in each system: Waldur keeps it as <c>host:port</c>
/// (e.g. <c>login.efp.discoverer.bg:2224</c>) while HEAppE keeps the bare host. The port is a
/// connection detail, not part of cluster identity, so the comparison is host-only.
/// </summary>
internal static class ClusterEndpointMatcher
{
    /// <summary>
    /// True when the cluster's <paramref name="masterNodeName"/> contains the Location's login
    /// <paramref name="endpoint"/> with any scheme and port stripped off. This keeps the long-standing
    /// permissive substring match (not an equality check) — the only thing this helper fixes versus the
    /// original is the port suffix, which made a <c>host:port</c> endpoint longer than the bare host and
    /// so never a substring. If the endpoint cannot be parsed as a URI, the raw endpoint is used, so
    /// malformed data degrades to the prior behavior instead of throwing.
    /// </summary>
    internal static bool MatchesMasterNode(string masterNodeName, string endpoint)
    {
        // The login endpoint can be null (the DB column is nullable). Nothing matches a missing
        // endpoint, and we must not throw.
        if (string.IsNullOrEmpty(endpoint))
        {
            return false;
        }

        // Strip the port (and any scheme) so a host:port endpoint can match HEAppE's bare host.
        // A bare host:port is not a valid absolute URI (the host is swallowed as a scheme, since dots
        // are legal in schemes), so prepend a scheme only when none is present; already-schemed
        // endpoints (http://, https://, frp://, ...) are left untouched. On a parse failure the raw
        // endpoint is used, preserving the original substring behavior.
        var candidate = endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : $"ssh://{endpoint}";
        var loginHost = Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri.Host : endpoint;

        return masterNodeName.Contains(loginHost, StringComparison.OrdinalIgnoreCase);
    }
}