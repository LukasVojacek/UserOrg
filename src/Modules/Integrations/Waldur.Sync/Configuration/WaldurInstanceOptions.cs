namespace Waldur.Sync.Configuration;

/// <summary>
/// Per-instance Waldur configuration. Mirrors the legacy <c>BaseIntegrationOptions</c> surface for
/// the API-client concern (auth, endpoints, rate limit, retry) plus the instance identity used as
/// the upsert discriminator. Mapper/consumer-only options (Heappe/storage/allocation maps) are
/// added by later blocks.
/// </summary>
public sealed class WaldurInstanceOptions
{
    /// <summary>Logical instance name / log + OTel tag, e.g. "EFP" or "Puhuri".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this instance is wired up. Disabled instances are ignored (and not validated).</summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute base address for the Waldur API, e.g. https://api.waldur.example/.</summary>
    public Uri? BaseUrl { get; set; }

    public Uri? CustomersEndpoint { get; set; }
    public Uri? ProjectsEndpoint { get; set; }
    public Uri? MarketplacePublicOfferingsEndpoint { get; set; }
    public Uri? MarketplaceResourcesEndpoint { get; set; }

    public string? AuthorizationHeader { get; set; }
    public string? AuthorizationHeaderValuePrefix { get; set; }
    public string? EndpointApiKey { get; set; }

    public TimeSpan? Timeout { get; set; }
    public int RateLimitPerSecond { get; set; } = 100;
    public int? RateLimitBurst { get; set; }
    public int RetryCount { get; set; } = 1;
    public int RetryInitialBackoffMs { get; set; } = 200;
    public int? RetryMaxDelayMs { get; set; }

    /// <summary>Bounded degree of parallelism for per-project / per-offering detail fan-out.</summary>
    public int MaxFanOutConcurrency { get; set; } = 4;

    /// <summary>
    /// Maximum number of items requested per page (<c>page_size</c> query parameter) when paging
    /// list endpoints (customers, projects, offerings, resources, users). Instances with large
    /// datasets may need a smaller value to stay under Waldur's own server-side page-size cap.
    /// </summary>
    public int PageSizeLimit { get; set; } = 100;

    /// <summary>Delay between sync iterations, measured from iteration completion.</summary>
    public int SyncIntervalMinutes { get; set; } = 30;

    // ── Identity (the upsert discriminator + service account) ────────────────
    public WaldurInstanceIdentityOptions Identity { get; set; } = new();

    // ── Mapper-only config (consumed by the pure mapper, not the API client) ──

    /// <summary>
    /// Per-provider HEAppE instance map. Key = provider (customer) GUID in <c>"N"</c> (dashless)
    /// format — mirrors the legacy <c>HeappeInstancesMapOptions.IdentifierUris</c> keying. HPC
    /// resources whose provider is present here get <c>HeappeUri</c>/<c>HeappeIdentifier</c> in the
    /// payload; the consumer throws when an HPC resource has no entry.
    /// </summary>
    public IDictionary<string, WaldurHeappeInstanceOptions> HeappeInstancesMap { get; set; } =
        new Dictionary<string, WaldurHeappeInstanceOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Waldur→Userorg allocation-unit conversion map (legacy <c>WaldurEfpConstants.UserorgWaldurAllocationUnitMap</c>):
    /// node-hours×1, core-hours→k-Hours×1000, GB→TB-hours×1000. Defaulted so both EFP and Puhuri
    /// behave identically unless overridden.
    /// </summary>
    public IList<WaldurAllocationUnitMapping> AllocationUnitMap { get; set; } =
        WaldurAllocationUnitMapping.Defaults();

    /// <summary>EFP-only storage-reconciliation config (drives <c>StorageConfigPayload</c>); null for non-EFP instances.</summary>
    public WaldurStorageOptions? Storage { get; set; }
}