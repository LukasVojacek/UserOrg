using System.ComponentModel.DataAnnotations;

namespace UserOrg.Gateway.Configuration;

/// <summary>
/// Configuration options for the API key authentication handler.
/// Values can be overridden via appsettings.json or environment variables,
/// e.g. <c>ApiKeyAuthentication__TimeoutSeconds=30</c>.
/// </summary>
internal sealed class ApiKeyAuthenticationOptions
{
    /// <summary>The configuration section name used to bind this options class.</summary>
    public const string SectionName = "ApiKeyAuthentication";

    /// <summary>
    /// Maximum number of seconds to wait for the Vault / cache lookup before canceling the request.
    /// Defaults to 15 seconds.
    /// </summary>
    [Range(1, 300, ErrorMessage = $"{nameof(TimeoutSeconds)} must be between 1 and 300.")]
    public int TimeoutSeconds { get; set; } = 15;
}