namespace UserOrg.Gateway.Features.Authorization;

/// <summary>
/// Service for matching user permissions against required permission templates.
/// Supports literal permissions and templates with placeholders (e.g., "perm:project:{name}:read").
/// </summary>
internal interface IPermissionMatcher
{
    /// <summary>
    /// Checks if user has at least one permission matching any of the required templates.
    /// </summary>
    /// <param name="permissionTemplates">Required permission templates (may contain placeholders like {name}).</param>
    /// <param name="userPermissions">User's permission claims.</param>
    /// <returns>True if user has at least one matching permission.</returns>
    bool HasRequiredPermission(IReadOnlyList<string> permissionTemplates, HashSet<string> userPermissions);

    /// <summary>
    /// Checks if user has at least one permission matching any of the required templates,
    /// substituting route values into placeholders before matching.
    /// When a placeholder (e.g., <c>{ProjectShortName}</c>) is found in <paramref name="routeValues"/>,
    /// it is replaced with the concrete path value before the check, turning the template into a literal
    /// and preventing access to resources the user does not belong to.
    /// Placeholders not present in <paramref name="routeValues"/> fall back to the regex wildcard behaviour.
    /// </summary>
    /// <param name="permissionTemplates">Required permission templates (may contain placeholders like {ProjectShortName}).</param>
    /// <param name="userPermissions">User's permission claims.</param>
    /// <param name="routeValues">Route values extracted from the request path (e.g., <c>{ "ProjectShortName": "myproject" }</c>).</param>
    /// <returns>True if user has at least one matching permission.</returns>
    bool HasRequiredPermission(IReadOnlyList<string> permissionTemplates, HashSet<string> userPermissions, IReadOnlyDictionary<string, string>? routeValues);
}