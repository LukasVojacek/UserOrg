using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Userorg.Adapter.Waldur.Sync.Handlers;

using Waldur.Contracts;
using Waldur.Contracts.Events;
using Waldur.Sync.Diff;

namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// DI registration seam for the Waldur event consumer (plan §4). Registers the in-process
/// publisher and leaves handler registration open for extension: later blocks (B07–B10) register
/// their <see cref="IWaldurEventHandler{TEvent}"/> implementations via
/// <see cref="AddWaldurEventHandler{TEvent, THandler}"/> without this seam knowing the concrete
/// handler types.
/// </summary>
internal static class WaldurEventConsumerExtensions
{
    /// <summary>Registers the in-process publisher. Handlers are added separately per event type.</summary>
    internal static IServiceCollection AddWaldurEventConsumers(this IServiceCollection services)
    {
        services.TryAddSingleton<IWaldurEventPublisher, InProcessWaldurEventPublisher>();
        return services;
    }

    /// <summary>
    /// Registers a scoped handler for a concrete event type. Scoped so each dispatched event gets a
    /// fresh handler bound to the per-event DI scope (ADR-0057: new scope = fresh DbContext).
    /// </summary>
    /// <typeparam name="TEvent">The concrete event the handler consumes.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    internal static IServiceCollection AddWaldurEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : WaldurIntegrationEvent
        where THandler : class, IWaldurEventHandler<TEvent>
    {
        services.AddScoped<IWaldurEventHandler<TEvent>, THandler>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="ProviderUpsertHandler"/> for both provider events (B07). Both
    /// <see cref="ProviderCreated"/> and <see cref="ProviderUpdated"/> route to the same upsert
    /// handler (ADR-0057/A1).
    /// </summary>
    internal static IServiceCollection AddWaldurProviderHandler(this IServiceCollection services)
    {
        services.AddWaldurEventHandler<ProviderCreated, ProviderUpsertHandler>();
        services.AddWaldurEventHandler<ProviderUpdated, ProviderUpsertHandler>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="ProjectUpsertHandler"/> for both project events (B08). Both
    /// <see cref="ProjectCreated"/> and <see cref="ProjectUpdated"/> route to the same upsert
    /// handler (ADR-0057/A1). Requires the per-instance <see cref="IWaldurConsumerOptions"/> seam
    /// (register via <see cref="AddWaldurConsumerInstanceOptions"/>) for config the payload cannot
    /// carry (domain-type name, Heappe auth type / PI email).
    /// </summary>
    internal static IServiceCollection AddWaldurProjectHandler(this IServiceCollection services)
    {
        services.AddWaldurEventHandler<ProjectCreated, ProjectUpsertHandler>();
        services.AddWaldurEventHandler<ProjectUpdated, ProjectUpsertHandler>();
        return services;
    }

    /// <summary>
    /// Registers the three membership handlers (B09) via the B06 seam: <see cref="UserAssignedHandler"/>,
    /// <see cref="UserRoleChangedHandler"/>, <see cref="UserRemovedHandler"/> (ADR-0057/A4). Requires an
    /// <c>IRoleListService</c> in the container for the role → <c>SystemRoleId</c> resolution.
    /// </summary>
    internal static IServiceCollection AddWaldurMembershipHandlers(this IServiceCollection services)
    {
        services.AddWaldurEventHandler<UserAssigned, UserAssignedHandler>();
        services.AddWaldurEventHandler<UserRoleChanged, UserRoleChangedHandler>();
        services.AddWaldurEventHandler<UserRemoved, UserRemovedHandler>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="StorageReconciliationHandler"/> for <see cref="StorageConfigurationChanged"/>
    /// (B10, ADR-0061 full-reconcile half) via the B06 seam. Sole owner of every
    /// <c>EfpCoreServiceName</c> storage row; storage config is carried wholly by the event payload,
    /// so no consumer-options registration is required for this handler.
    /// </summary>
    internal static IServiceCollection AddWaldurStorageReconciliationHandler(this IServiceCollection services)
    {
        services.AddWaldurEventHandler<StorageConfigurationChanged, StorageReconciliationHandler>();
        return services;
    }

    /// <summary>
    /// Registers the adapter-side cold-rebuild port (B11, ADR-0055/D1) plus the (instance name →
    /// ServiceName) mapping it needs. Call once per enabled instance; the <see cref="WaldurShaMapRebuilder"/>
    /// is a singleton (the worker resolves it at root level) and creates its own DI scope per rebuild.
    /// </summary>
    internal static IServiceCollection AddWaldurShaMapRebuilder(
        this IServiceCollection services,
        string instance,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddSingleton(new WaldurRebuildInstance(instance, serviceName));
        services.TryAddSingleton<IShaMapRebuilder, WaldurShaMapRebuilder>();
        return services;
    }

    /// <summary>
    /// Registers the consumer-side per-instance options resolver (ADR-0057/A5 extension). Call once
    /// per enabled instance; the resolver keys them by
    /// <see cref="WaldurConsumerInstanceOptions.ServiceName"/> so shared handlers (B08–B10) stay
    /// payload-driven for Waldur data while reading instance config here.
    /// </summary>
    internal static IServiceCollection AddWaldurConsumerInstanceOptions(
        this IServiceCollection services,
        WaldurConsumerInstanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.TryAddSingleton<IWaldurConsumerOptions, WaldurConsumerOptions>();
        return services;
    }
}