namespace Waldur.Sync.Configuration;

/// <summary>A single configured storage location (legacy <c>StorageInstanceConfiguration</c>/<c>IrodsInstanceConfiguration</c>).</summary>
public sealed class WaldurStorageInstanceOptions
{
    public string Name { get; set; } = string.Empty;
    public Uri? Endpoint { get; set; }
    public int? AllocationAmount { get; set; }
}