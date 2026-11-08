namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Configuration for the Role Management feature HTTP calls.
/// </summary>
internal sealed class RoleManagementOptions
{
    public const string SectionName = "RoleManagement";

    /// <summary>
    /// Optional path to a JSON seed file for initial RBAC data import.
    /// When set, the application seeds the database on startup if no roles exist yet.
    /// In Docker deployments the file is embedded at <c>/app/rbac/seed.json</c> by default.
    /// Configure via <c>RoleManagement__SeedFilePath</c> environment variable or
    /// <c>RoleManagement:SeedFilePath</c> in appsettings.json.
    /// </summary>
    public string SeedFilePath { get; set; } = "/app/rbac/seed.json";

    /// <summary>
    /// Access token required for initial authentication to /role-management endpoint.
    /// This should be set via environment variable ROLEMANAGEMENT__ACCESSTOKEN or in appsettings.json.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Cookie expiration time in minutes. Default is 30 minutes.
    /// </summary>
    public int CookieExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Name of the authentication cookie. Default is "RoleManagementAuth".
    /// </summary>
    public string CookieName { get; set; } = "RoleManagementAuth";

    /// <summary>
    /// Optional path prefix when the gateway is hosted under a sub-path (e.g., "/userorg").
    /// This is used to render correct base href for Blazor and preserve redirects.
    /// Configure via RoleManagement:PathPrefix or Gateway:PathBase.
    /// </summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Validates option values and throws when invalid to fail fast on startup.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            throw new ArgumentException(
                "RoleManagement AccessToken is not configured. Set ROLEMANAGEMENT__ACCESSTOKEN environment variable or configure RoleManagement:AccessToken in appsettings.json",
                nameof(AccessToken));
        }

        if (CookieExpirationMinutes <= 0)
        {
            throw new ArgumentException("CookieExpirationMinutes must be greater than 0", nameof(CookieExpirationMinutes));
        }

        if (string.IsNullOrWhiteSpace(CookieName))
        {
            throw new ArgumentException("CookieName cannot be null or empty", nameof(CookieName));
        }
    }
}