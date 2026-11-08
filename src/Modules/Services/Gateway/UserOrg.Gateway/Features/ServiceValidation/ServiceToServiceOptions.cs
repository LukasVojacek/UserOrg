namespace UserOrg.Gateway.Features.ServiceValidation;

/// <summary>
/// Configuration options for service-to-service authentication and authorization.
/// </summary>
internal sealed class ServiceToServiceOptions
{
    public const string ConfigurationSection = "ServiceToService";

    /// <summary>
    /// List of route configurations defining which callers are allowed for each target service.
    /// </summary>
    public List<ServiceRoute> Routes { get; set; } = new();
}

/// <summary>
/// Represents a single route configuration for a target service.
/// </summary>
internal sealed class ServiceRoute
{
    /// <summary>
    /// The target service name (e.g., "forge-api").
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// List of service names allowed to call this target (e.g., ["userorg", "gateway"]).
    /// </summary>
    public List<string> AllowedCallers { get; set; } = new();
}