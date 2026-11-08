using Microsoft.Extensions.Hosting;

namespace RemI;

/// <summary>
/// Composition seam for the RemI service module. The module is a read-only facing surface over the host's
/// userorg data, reached through <see cref="HostContracts.IUserorgService"/>, which the host implements and
/// registers in <c>RemIAdapterExtensions</c>. The module owns no services of its own today; the seam exists
/// so that any future module-owned wiring lands here rather than in the adapter (the <c>AddDdi</c> pattern).
/// </summary>
public static class RemIIntegrationExtensions
{
    /// <summary>
    /// Registers the RemI module's own services on the host.
    /// </summary>
    /// <param name="hostBuilder">The host application builder used as the composition root.</param>
    /// <returns>The same <paramref name="hostBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddRemIIntegration(this IHostApplicationBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);

        return hostBuilder;
    }
}