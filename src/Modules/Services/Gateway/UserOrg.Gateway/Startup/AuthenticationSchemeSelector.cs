using Microsoft.Extensions.Options;

using UserOrg.Gateway.Configuration;

namespace UserOrg.Gateway.Startup;

/// <summary>
/// The forward selector of the dynamic policy scheme. Priority order: role-management paths go to the cookie
/// scheme, public endpoints to the public scheme, then the Authorization header decides, and a request
/// without one falls back to Bearer (which answers 401 without a valid token).
/// </summary>
internal static class AuthenticationSchemeSelector
{
    private static readonly string[] RoleManagementPrefixes =
        ["/role-management", "/_framework", "/api/role-management/rbac/pdf", "/_blazor"];

    /// <summary>
    /// The scheme a request is forwarded to. <paramref name="isDevelopment"/> gates the Develop-only override
    /// (ADR-0045 gate 1): outside Development the override scheme is never selected.
    /// </summary>
    internal static string Select(HttpContext context, bool isDevelopment)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // A role-management path never falls back: without the cookie the cookie scheme answers 401.
        if (IsRoleManagementPath(path))
        {
            return Constants.RoleManagementCookieSchemeId;
        }

        if (IsPublicRequest(context, path))
        {
            return Constants.PublicWithInternalTokenSchemeId;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        return string.IsNullOrEmpty(authHeader)
            ? Constants.BearerSchemeId
            : SelectByAuthorizationHeader(context, authHeader, isDevelopment);
    }

    private static bool IsRoleManagementPath(string path)
        => RoleManagementPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsPublicRequest(HttpContext context, string path)
    {
        var publicEndpointsOptions = context.RequestServices.GetService<IOptions<PublicEndpointsOptions>>()?.Value;
        return publicEndpointsOptions != null && IsPublicEndpoint(path, publicEndpointsOptions);
    }

    private static bool IsPublicEndpoint(string path, PublicEndpointsOptions options)
    {
        // Normalize trailing slash so "/api" and "/api/" match the same patterns
        var normalizedPath = path.Length > 1 ? path.TrimEnd('/') : path;
        return options.Paths.Any(p => p.MatchType switch
        {
            PathMatchType.Exact => string.Equals(normalizedPath, p.Pattern, StringComparison.OrdinalIgnoreCase),
            PathMatchType.Prefix => normalizedPath.StartsWith(p.Pattern, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }

    private static string SelectByAuthorizationHeader(HttpContext context, string authHeader, bool isDevelopment)
    {
        // Service, Provider and any other prefix all go to the API key scheme.
        if (!AuthorizationPrefix(authHeader).Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return Constants.ApiKeySchemeId;
        }

        return IsDevOverrideEnabled(context, isDevelopment) ? Constants.DevOverrideSchemeId : Constants.BearerSchemeId;
    }

    private static string AuthorizationPrefix(string authHeader)
    {
        var firstSpace = authHeader.IndexOf(' ', StringComparison.Ordinal);
        return firstSpace > 0 ? authHeader[..firstSpace] : authHeader;
    }

    /// <summary>
    /// Develop-only override (ADR-0045 gate 2): hijack a bearer request only when the override is explicitly
    /// enabled. The bearer string then carries the caller's idpid; when disabled (default), a real IdP token
    /// flows through normally. The options are only consulted in Development.
    /// </summary>
    private static bool IsDevOverrideEnabled(HttpContext context, bool isDevelopment)
        => isDevelopment
            && context.RequestServices.GetService<IOptions<DevAuthOverrideOptions>>()?.Value is { Enabled: true };
}