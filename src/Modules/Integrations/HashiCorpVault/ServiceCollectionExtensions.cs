using OpenTelemetry.Trace;

namespace HashiCorpVault;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenTelemetry tracing for HashiCorp Vault integration.
    /// Call this method on your TracerProviderBuilder to enable collection for Vault activities.
    /// </summary>
    public static TracerProviderBuilder AddHashiCorpVaultInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(VaultTelemetry.ActivitySourceName);
    }
}