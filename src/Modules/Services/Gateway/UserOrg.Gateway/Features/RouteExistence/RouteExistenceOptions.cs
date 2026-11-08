namespace UserOrg.Gateway.Features.RouteExistence;

/// <summary>
/// Configuration of the route-existence oracle (<c>RouteExistence:</c> section, ADR-0106).
/// </summary>
internal sealed class RouteExistenceOptions
{
    public const string SectionName = "RouteExistence";

    /// <summary>
    /// Whether the oracle loads at all. Disabled, nothing is ever published and every path keeps the
    /// answer it had before ADR-0106 - the kill switch for the 404.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often a loaded surface is reconciled against the published documents.</summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait before retrying after a load that did not complete.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Timeout of a single document fetch.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
}