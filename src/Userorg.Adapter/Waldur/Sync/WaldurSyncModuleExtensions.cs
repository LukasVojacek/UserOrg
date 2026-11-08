using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Userorg.Adapter.Waldur.Sync;

using Waldur.Sync;
using Waldur.Sync.Configuration;

namespace Userorg.Adapter;

/// <summary>
/// Composition root for the event-driven Waldur.Sync module (B12 wire-up). Registers the producer
/// side (per-instance sync workers + API clients, from <see cref="WaldurSyncServiceCollectionExtensions"/>)
/// alongside the in-process consumer side (publisher + event handlers + per-instance seams). Gated on
/// <c>Waldur:Instances</c>: zero enabled instances registers nothing.
/// </summary>
public static class WaldurSyncModuleExtensions
{
    /// <summary>
    /// Registers the Waldur.Sync producer + consumer for every enabled instance in
    /// <paramref name="configuration"/>. No enabled instance registers nothing.
    /// </summary>
    public static IServiceCollection AddWaldurSyncModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = (configuration.GetSection(WaldurSyncOptions.SectionName).Get<WaldurSyncOptions>() ?? new WaldurSyncOptions())
            .EnabledInstances.ToList();

        if (enabled.Count == 0)
        {
            return services;
        }

        // Producer: one sequential worker + typed rate-limited API client per enabled instance, plus
        // WaldurSyncOptions binding + validation (ADR-0056 / B02).
        services.AddWaldurSync(configuration);

        // Consumer: the in-process publisher (B06) + every handler (B07–B10) via the B06 seam.
        services.AddWaldurEventConsumers();
        services.AddWaldurProviderHandler();
        services.AddWaldurProjectHandler();
        services.AddWaldurMembershipHandlers();
        services.AddWaldurStorageReconciliationHandler();

        // Per-instance consumer seams: the cold-rebuild (instance → ServiceName) map (B11) and the
        // consumer-options resolver (B08) for config the event payload cannot carry.
        foreach (var instance in enabled)
        {
            // ServiceName is required + validated for every enabled instance (WaldurSyncOptionsValidator).
            var serviceName = instance.Identity.ServiceName!;

            services.AddWaldurShaMapRebuilder(instance.Name, serviceName);
            services.AddWaldurConsumerInstanceOptions(new WaldurConsumerInstanceOptions(
                serviceName,
                instance.Identity.ServiceProjectDomainName,
                instance.Identity.WaldurPreferredAuthType ?? string.Empty,
                instance.Identity.WaldurPIEmail!));

            services.AddSingleton(new WaldurServiceAccountIdentity(
                serviceName,
                instance.Identity.ServiceAccountUserName!,
                instance.Identity.ServiceAccountEmail!));
        }

        // Bootstraps every enabled instance's service account before the Waldur instance processing
        services.AddHostedService<WaldurServiceAccountBootstrapper>();

        return services;
    }
}