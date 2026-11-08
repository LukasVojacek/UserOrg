using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntegrationTests.AuthMatrix;

/// <summary>
/// Parses roleManagmentRBAC.json into the two lookups RbacExpectationBuilder joins against: required
/// permissions per route, and granted permissions per role.
/// </summary>
internal sealed class RbacDocument
{
    private static readonly Regex RouteParameterPattern = new(@"\{[^{}]+\}", RegexOptions.Compiled);

    private readonly Dictionary<(string HttpMethod, string NormalizedPath), IReadOnlyList<string>> _requiredPermissionsByRoute;
    private readonly Dictionary<string, IReadOnlySet<string>> _permissionsByRole;

    private RbacDocument(
        Dictionary<(string, string), IReadOnlyList<string>> requiredPermissionsByRoute,
        Dictionary<string, IReadOnlySet<string>> permissionsByRole)
    {
        _requiredPermissionsByRoute = requiredPermissionsByRoute;
        _permissionsByRole = permissionsByRole;
    }

    internal static RbacDocument Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var features = document.RootElement.GetProperty("features");

        var requiredPermissionsByRoute = new Dictionary<(string, string), IReadOnlyList<string>>();
        foreach (var mapping in features.GetProperty("endpointMappings").GetProperty("items").EnumerateArray())
        {
            var method = mapping.GetProperty("httpMethod").GetString()!.ToUpperInvariant();
            var normalizedPath = Normalize(mapping.GetProperty("pathTemplate").GetString()!);
            var permissions = mapping.GetProperty("permissionKeys").EnumerateArray()
                .Select(key => key.GetString()!)
                .ToArray();

            // First mapping wins on a (method, normalized path) collision. The export carries
            // placeholder "/-" entries (unrouted permissions like perm:heappe:exec) that all
            // normalize to the same key but never match a real discovered route.
            requiredPermissionsByRoute.TryAdd((method, normalizedPath), permissions);
        }

        var permissionsByRole = new Dictionary<string, IReadOnlySet<string>>();
        foreach (var role in features.GetProperty("roles").GetProperty("items").EnumerateArray())
        {
            var name = role.GetProperty("name").GetString()!;
            var permissions = role.GetProperty("permissionKeys").EnumerateArray()
                .Select(key => key.GetString()!)
                .ToHashSet();
            permissionsByRole[name] = permissions;
        }

        return new RbacDocument(requiredPermissionsByRoute, permissionsByRole);
    }

    /// <summary>The route's required permission keys, or null if the route has no RBAC mapping.</summary>
    internal IReadOnlyList<string>? FindRequiredPermissions(Route route) =>
        _requiredPermissionsByRoute.TryGetValue((route.HttpMethod, Normalize(route.Path)), out var permissions)
            ? permissions
            : null;

    internal IReadOnlySet<string> PermissionsForRole(string role) =>
        _permissionsByRole.TryGetValue(role, out var permissions) ? permissions : new HashSet<string>();

    // roleManagmentRBAC.json's path templates don't always use the same placeholder names as the
    // live route (e.g. "/api/v2/ProjectTags/{id}" vs. the route's "{tagName}") — routes are matched
    // by placeholder position, not name.
    private static string Normalize(string pathTemplate) => RouteParameterPattern.Replace(pathTemplate, "{}");
}

/// <summary>
/// Computes the expected authorization outcome for a discovered v2 route + role tier by joining
/// against the gateway's RBAC export (roleManagmentRBAC.json, via <see cref="RbacDocument"/>) and its
/// public-endpoint allowlist.
/// </summary>
internal static class RbacExpectationBuilder
{
    internal const string AnonymousRole = "Anonymous";

    private const string PublicV2PathPrefix = "/api/v2/Public";

    private const string PublicV2VersionPath = "/api/v2/Version";

    internal static ExpectedOutcome Expect(Route route, string role, RbacDocument rbac)
    {
        ArgumentNullException.ThrowIfNull(rbac);

        if (role == AnonymousRole)
        {
            return route.Path.StartsWith(PublicV2PathPrefix, StringComparison.Ordinal)
                || route.Path == PublicV2VersionPath
                ? ExpectedOutcome.Allowed
                : ExpectedOutcome.Unauthorized401;
        }

        var requiredPermissions = rbac.FindRequiredPermissions(route);
        if (requiredPermissions is null)
        {
            // Gateway fails closed on a route it has no RBAC mapping for, regardless of the
            // authenticated caller's own permissions.
            return ExpectedOutcome.Forbidden403;
        }

        if (requiredPermissions.Count == 0)
        {
            return ExpectedOutcome.Allowed;
        }

        var rolePermissions = rbac.PermissionsForRole(role);
        return requiredPermissions.Any(rolePermissions.Contains)
            ? ExpectedOutcome.Allowed
            : ExpectedOutcome.Forbidden403;
    }
}
