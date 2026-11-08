using HashiCorpVault.Contracts.Models;

namespace HashiCorpVault.Contracts.Configuration;

/// <summary>
/// Maps a named <see cref="ExternalServiceAccessCredentials"/> entry stored in HashiCorp Vault
/// to a target key in the application's <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> tree.
/// </summary>
public sealed record ExternalCredentialMapping
{
    /// <summary>Gets the <see cref="ExternalServiceAccessCredentials.Name"/> of the Vault entry to read.</summary>
    public required string CredentialName { get; init; }

    /// <summary>Gets the configuration key to populate (e.g. <c>"Waldur:EndpoindApiKey"</c>).</summary>
    public required string TargetConfigKey { get; init; }

    /// <summary>Gets the selector that extracts the desired field from the Vault credential.</summary>
    public required Func<ExternalServiceAccessCredentials, string?> FieldSelector { get; init; }
}