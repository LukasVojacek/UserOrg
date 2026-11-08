namespace Waldur.Sync.Configuration;

/// <summary>Instance identity: the upsert discriminator and the sync service account.</summary>
public sealed class WaldurInstanceIdentityOptions
{
    /// <summary>THE upsert-key discriminator, e.g. "EFP-Waldur" / "Puhuri-Waldur".</summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Name of the <c>ProjectDomainType</c> stamped on consumer-created projects (legacy
    /// <c>WaldurConstants.ServiceProjectDomainName</c>). Defaulted to "External" — both EFP and Puhuri
    /// used the same value — but overridable per instance.
    /// </summary>
    public string ServiceProjectDomainName { get; set; } = "External";

    public string? ServiceAccountUserName { get; set; }
    public string? ServiceAccountEmail { get; set; }
    public string? WaldurPIEmail { get; set; }
    public string? WaldurPreferredAuthType { get; set; }
}