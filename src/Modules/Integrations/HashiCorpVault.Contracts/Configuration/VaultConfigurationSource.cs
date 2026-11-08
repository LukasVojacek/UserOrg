using Microsoft.Extensions.Configuration;

namespace HashiCorpVault.Contracts.Configuration;

/// <summary>
/// Configuration source that loads secrets from a HashiCorp Vault KV-v2 mount
/// into the <see cref="IConfiguration"/> tree at application startup.
/// </summary>
public sealed class VaultConfigurationSource : IConfigurationSource
{
    /// <summary>Gets the base address of the Vault (or Vault proxy) HTTP endpoint.</summary>
    public required string VaultAddress { get; init; }

    /// <summary>Gets the KV-v2 mount point name (e.g. <c>"UserOrg_kv"</c>).</summary>
    public required string MountPoint { get; init; }

    public string? AddressPrefix { get; set; }

    /// <summary>Gets the list of <see cref="ExternalCredentialMapping"/> entries to resolve.</summary>
    public IList<ExternalCredentialMapping> Mappings { get; } = new List<ExternalCredentialMapping>();

    /// <inheritdoc/>
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new VaultConfigurationProvider(this);
}