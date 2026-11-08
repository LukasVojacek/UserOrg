using UserOrg.Shared.OpenApi;

namespace UserOrg.Gateway.Configuration;

/// <summary>
/// Configuration for public endpoints that allow anonymous access
/// but require internal token generation for downstream services.
/// </summary>
internal sealed class PublicEndpointsOptions
{
    public const string SectionName = "PublicEndpoints";

    /// <summary>
    /// Gets or sets the list of public endpoint paths.
    /// Default paths are always included: /api (Scalar UI), its static assets, /health, and /alive.
    /// </summary>
    public IReadOnlyList<PublicEndpointPath> Paths { get; set; } = GetDefaultPaths(true);

    /// <summary>
    /// Returns default public endpoint paths that are always available.
    /// </summary>
    internal static IReadOnlyList<PublicEndpointPath> GetDefaultPaths(bool isV1ApiEnabled) =>
    [
        // Scalar API reference UI (served at /{PrefixDocPath})
        new PublicEndpointPath { Pattern = "/api", MatchType = PathMatchType.Exact },
        new PublicEndpointPath { Pattern = "/api/scalar.js", MatchType = PathMatchType.Exact },
        new PublicEndpointPath { Pattern = "/api/scalar.aspnetcore.js", MatchType = PathMatchType.Exact },
        // OpenAPI spec endpoints — targeted exact paths to avoid exposing all /api/* routes
        .. ApiDocuments.Published(isV1ApiEnabled).Select(documentName =>
            new PublicEndpointPath { Pattern = $"/api/{documentName}/openapi.json", MatchType = PathMatchType.Exact }),
        new PublicEndpointPath { Pattern = "/api/Public", MatchType = PathMatchType.Prefix },
        new PublicEndpointPath { Pattern = "/api/v2/Public", MatchType = PathMatchType.Prefix },
        new PublicEndpointPath { Pattern = "/favicon.ico", MatchType = PathMatchType.Exact },
        new PublicEndpointPath { Pattern = "/health", MatchType = PathMatchType.Prefix },
        new PublicEndpointPath { Pattern = "/alive", MatchType = PathMatchType.Exact },
        new PublicEndpointPath { Pattern = "/api/version", MatchType = PathMatchType.Exact },
        new PublicEndpointPath { Pattern = "/api/v2/Version", MatchType = PathMatchType.Exact }
    ];
}

/// <summary>
/// Represents a single public endpoint path configuration.
/// </summary>
internal sealed record PublicEndpointPath
{
    /// <summary>
    /// Path pattern to match (e.g., "/userorg/swagger").
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Type of matching: Exact or Prefix.
    /// </summary>
    public required PathMatchType MatchType { get; init; }
}

/// <summary>
/// Defines how paths are matched against the pattern.
/// </summary>
internal enum PathMatchType
{
    /// <summary>
    /// Path must exactly match the pattern.
    /// </summary>
    Exact,

    /// <summary>
    /// Path must start with the pattern.
    /// </summary>
    Prefix
}