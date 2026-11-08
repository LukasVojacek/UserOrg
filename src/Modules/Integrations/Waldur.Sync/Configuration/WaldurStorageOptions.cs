namespace Waldur.Sync.Configuration;

/// <summary>EFP storage-reconciliation configuration (legacy EFP iRODS/staging map options).</summary>
public sealed class WaldurStorageOptions
{
    /// <summary>Service name owning reconciliation-created rows (legacy <c>WaldurEfpConstants.EfpCoreServiceName</c>, "EFP-Core").</summary>
    public string EfpCoreServiceName { get; set; } = string.Empty;

    public bool UseSharedIrodsForAllEntities { get; set; }

    /// <summary>The shared iRODS instance, if configured.</summary>
    public WaldurStorageInstanceOptions? SharedIrods { get; set; }

    /// <summary>Per-provider iRODS instances, keyed by provider GUID ("N" format).</summary>
    public IDictionary<string, WaldurStorageInstanceOptions> IrodsInstancesMap { get; set; } =
        new Dictionary<string, WaldurStorageInstanceOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-provider staging-area instances, keyed by provider GUID ("N" format).</summary>
    public IDictionary<string, WaldurStorageInstanceOptions> StagingAreaInstancesMap { get; set; } =
        new Dictionary<string, WaldurStorageInstanceOptions>(StringComparer.OrdinalIgnoreCase);
}