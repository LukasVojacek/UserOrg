namespace Core.Features.User.InactiveUserSync;

/// <summary>
/// Configuration options for the inactive user sync background service.
/// Fetches list of invalid/inactive users from DDI/DRE and marks them as deleted.
/// Uses OAuth2 client_credentials flow for authentication.
/// </summary>
internal sealed class InactiveUserSyncOptions
{
    /// <summary>Configuration section name for binding.</summary>
    public const string ConfigurationPath = "InactiveUserSync";

    /// <summary>
    /// Base URL for AAI (Authentication & Authorization Infrastructure) realm.
    /// Full endpoints are constructed from this base URL.
    /// Example: https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// OAuth2 client ID for client_credentials flow.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// OAuth2 client secret for client_credentials flow.
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Sync interval (default 7 days = 1 week).
    /// </summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Enable or disable the inactive user sync background service (default false).
    /// Set to false to disable syncing in environments where it's not needed.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Constructs the OAuth2 token endpoint URL from the base URL.
    /// </summary>
    public string GetTokenUri() => $"{BaseUrl}/protocol/openid-connect/token";

    /// <summary>
    /// Constructs the disabled users endpoint URL from the base URL.
    /// </summary>
    public string GetDisabledUsersUri() => $"{BaseUrl}/users-disabled-info";
}