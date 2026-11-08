using Microsoft.Extensions.Configuration;

namespace HashiCorpVault.Contracts.Configuration;

/// <summary>
/// Extension methods for registering the HashiCorp Vault configuration source
/// with an <see cref="IConfigurationBuilder"/>.
/// </summary>
public static class VaultConfigurationBuilderExtensions
{
    /// <summary>
    /// Adds a HashiCorp Vault KV-v2 configuration source that resolves
    /// <see cref="ExternalCredentialMapping"/> entries and injects their values
    /// into the <see cref="IConfiguration"/> tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Vault address and mount point are resolved automatically from the
    /// configuration sources already registered on <paramref name="builder"/>, in this order:
    /// <list type="number">
    ///   <item><description><c>VaultCredentials:Address</c> — direct address (non-Aspire / production)</description></item>
    ///   <item><description><c>Services:vault:http</c> — Aspire service-discovery endpoint</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Authentication is handled by the Vault proxy agent; the application does not need a Vault token.
    /// </para>
    /// </remarks>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to add to.</param>
    /// <param name="configure">A delegate to configure which credentials and target keys to map.</param>
    /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither <c>VaultCredentials:Address</c> nor <c>Services:vault:http</c> is present
    /// in the already-registered configuration sources.
    /// </exception>
    public static IConfigurationBuilder AddVaultSecrets(
        this IConfigurationBuilder builder,
        Action<VaultConfigurationSource> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var tempConfig = builder.Build();

        var vaultAddress = tempConfig["VaultCredentials:Address"]
            ?? tempConfig["Services:vault:http"]
            ?? throw new InvalidOperationException(
                "Vault address is not configured. Set 'VaultCredentials:Address' or, for Aspire environments, 'Services:vault:http'.");

        var mountPoint = tempConfig["VaultCredentials:MountPoint"] ?? throw new InvalidOperationException(
            "Vault mount point is not configured. Set 'VaultCredentials:MountPoint' in the configuration.");

        var addressPrefix = tempConfig["VaultCredentials:AddressPrefix"];

        var source = new VaultConfigurationSource
        {
            VaultAddress = vaultAddress,
            MountPoint = mountPoint,
            AddressPrefix = addressPrefix
        };

        configure(source);

        return builder.Add(source);
    }
}