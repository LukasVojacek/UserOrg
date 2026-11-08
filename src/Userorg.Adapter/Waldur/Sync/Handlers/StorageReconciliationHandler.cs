using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base;
using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Consumer-side full storage reconcile for one EFP instance (ADR-0061, plan §4.5). Sole owner of
/// every reconciliation-created row (<c>ServiceName == EfpCoreServiceName</c>): the synthetic
/// EFP-Core provider, the shared iRODS location, the per-provider iRODS/StagingArea locations, and
/// the storage <see cref="AssignedLocation"/>s that hang off the instance's resources. B08's project
/// upsert deliberately writes NO storage rows and defers everything here (developer-approved
/// Option B), so this handler owns BOTH the storage-location lifecycle AND the resource→storage
/// association sweep.
/// </summary>
/// <remarks>
/// Ports the behaviour of the old <c>WaldurEfpExporter</c> storage path into a single event handler
/// with one terminal <see cref="IUserOrgDbContext.SaveChangesAsync"/> (ADR-0057/A3, no per-resource
/// manual transactions):
/// <list type="bullet">
/// <item><c>GetOrCreateEfpCoreProviderAsync</c> → <see cref="EnsureEfpCoreProviderAsync"/>: ensure /
/// undelete the EFP-Core provider keyed by <c>EfpCoreServiceName</c>;</item>
/// <item><c>InitializationIrodsAndStagingAreaLocations</c> / <c>EnsureStorageLocationAsync</c> /
/// <c>SyncProviderStorageLocationsAsync</c> → <see cref="ReconcileLocationsAsync"/>: ensure /
/// undelete the shared iRODS and per-provider iRODS + StagingArea locations, and
/// <b>deactivate-on-config-removal</b> when a mapping disappeared from config;</item>
/// <item><c>AssociateStorageLocationsToProjectResources</c> + <c>HandleSharedIrodsAsync</c> /
/// <c>AssociateProviderIrodsToResourceAsync</c> / <c>AssociateStagingAreaToResourceAsync</c> /
/// <c>CreateAssignedLocationFromLocation</c> → <see cref="AssociateResourcesAsync"/>: sweep every
/// resource of the instance and attach / undelete / deactivate its EFP-Core assigned locations per
/// the current config (<c>UseSharedIrodsForAllEntities</c> switch + allocation amounts).</item>
/// </list>
/// Ownership + idempotency: every row written or mutated here carries / is filtered by
/// <c>EfpCoreServiceName</c>, so a rerun on unchanged config is a no-op (existing rows are matched by
/// <c>LocationId</c> and undeleted / amount-synced instead of duplicated) and a non-EFP-Core
/// <see cref="AssignedLocation"/>/<see cref="Location"/> is never touched. The service account is
/// LOOKED UP (never ensured — B06/F1); a missing one throws (not acked → replay). Storage config
/// comes wholly from the event's <see cref="StorageConfigPayload"/> — the consumer-options seam is
/// not needed here.
/// </remarks>
internal sealed class StorageReconciliationHandler(
    IUserOrgDbContext context,
    ILogger<StorageReconciliationHandler> logger)
    : IWaldurEventHandler<StorageConfigurationChanged>
{
    private const string IrodsAllocationUnitTypeName = "GB";
    private const string StagingAllocationUnitTypeName = "unspecified";

    public async Task<Result<Error>> HandleAsync(StorageConfigurationChanged evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var config = evt.Config;
        var integrationName = evt.Instance;
        var serviceName = evt.ServiceName;
        var efpCoreServiceName = config.EfpCoreServiceName;

        var accountResult = await context.RequireServiceAccountAsync(integrationName, serviceName, ct);
        if (!accountResult.IsSuccess)
        {
            return Result<Error>.Failure(accountResult.Error);
        }

        logger.LogInformation("{Integration}: reconciling EFP storage configuration.", integrationName);

        var resolved = await ReconcileLocationsAsync(config, serviceName, efpCoreServiceName, ct);
        await AssociateResourcesAsync(config, serviceName, efpCoreServiceName, resolved, ct);

        // ADR-0057/A3: single terminal save — the whole reconcile commits atomically or replays.
        await context.SaveChangesAsync(ct);
        return Result<Error>.Success();
    }

    /// <summary>
    /// Location lifecycle (ports <c>InitializationIrodsAndStagingAreaLocations</c>): ensure/undelete
    /// the EFP-Core provider + shared iRODS, and per instance-owned provider ensure/undelete the
    /// configured iRODS/StagingArea locations, deactivating any whose mapping left the config. Returns
    /// the active locations keyed for the association sweep — nothing here queries a row the sweep
    /// then needs, so no intermediate save is required.
    /// </summary>
    private async Task<ResolvedStorage> ReconcileLocationsAsync(
        StorageConfigPayload config, string serviceName, string efpCoreServiceName, CancellationToken ct)
    {
        var efpCoreProvider = await EnsureEfpCoreProviderAsync(config, efpCoreServiceName, ct);

        Location? sharedIrods = null;
        if (config.SharedIrods is null)
        {
            await DeactivateProviderWithLocationsAsync(efpCoreProvider, efpCoreServiceName, ct);
        }
        else
        {
            sharedIrods = await EnsureStorageLocationAsync(efpCoreProvider, config.SharedIrods, ELocationType.iRODS, efpCoreServiceName, ct);
        }

        var irodsByProvider = ByProviderGuid(config.IrodsInstances);
        var stagingByProvider = ByProviderGuid(config.StagingAreaInstances);

        var providers = await context.ProvidersQuery
            .Where(p => !p.IsDeleted && p.ServiceName == serviceName)
            .ToListAsync(ct);

        var perProvider = new Dictionary<Guid, ProviderStorage>();
        foreach (var provider in providers)
        {
            if (!Guid.TryParse(provider.ExternalId, out var providerGuid))
            {
                // Faithful to the old sweep: a provider whose ExternalId is not a GUID cannot be
                // matched to config; fail so the event is not acked and replays.
                throw new InvalidOperationException(
                    $"Provider '{provider.Id}' has a non-GUID ExternalId '{provider.ExternalId}'.");
            }

            var irods = irodsByProvider.TryGetValue(providerGuid, out var irodsCfg)
                ? await EnsureStorageLocationAsync(provider, irodsCfg, ELocationType.iRODS, efpCoreServiceName, ct)
                : await DeactivateProviderLocationsOfTypeAsync(provider.Id, ELocationType.iRODS, efpCoreServiceName, ct);

            var staging = stagingByProvider.TryGetValue(providerGuid, out var stagingCfg)
                ? await EnsureStorageLocationAsync(provider, stagingCfg, ELocationType.Storage, efpCoreServiceName, ct)
                : await DeactivateProviderLocationsOfTypeAsync(provider.Id, ELocationType.Storage, efpCoreServiceName, ct);

            perProvider[provider.Id] = new ProviderStorage(irods, irodsCfg?.AllocationAmount, staging);
        }

        return new ResolvedStorage(efpCoreProvider, sharedIrods, config.SharedIrods?.AllocationAmount, perProvider);
    }

    /// <summary>
    /// Association sweep (ports <c>AssociateStorageLocationsToProjectResources</c>): for every
    /// resource of the instance, attach/undelete/deactivate its EFP-Core assigned locations against
    /// the resolved config. Only <c>EfpCoreServiceName</c> assigned locations are ever mutated.
    /// </summary>
    private async Task AssociateResourcesAsync(
        StorageConfigPayload config, string serviceName, string efpCoreServiceName, ResolvedStorage resolved, CancellationToken ct)
    {
        var gbType = await FindAllocationUnitTypeAsync(IrodsAllocationUnitTypeName, ct);
        var unspecifiedType = await FindAllocationUnitTypeAsync(StagingAllocationUnitTypeName, ct);

        var resources = await context.ProjectResourcesQuery
            .Include(r => r.AssignedLocations).ThenInclude(al => al.Location).ThenInclude(l => l!.LocationType)
            .Include(r => r.AssignedLocations).ThenInclude(al => al.Location).ThenInclude(l => l!.Provider)
            .Where(r => r.ServiceName == serviceName)
            .ToListAsync(ct);

        foreach (var resource in resources)
        {
            // The resource's HPC provider comes from its active HPC assigned location (B08-written).
            var provider = resource.AssignedLocations
                .FirstOrDefault(a => !a.IsDeleted
                    && a.Location?.LocationType?.Id == (int)ELocationType.HPC
                    && a.ServiceName == serviceName)
                ?.Location?.Provider;

            if (provider is null)
            {
                // No HPC provider ⇒ the resource has no storage: deactivate all its EFP-Core iRODS +
                // staging associations.
                DeactivateAssigned(resource, efpCoreServiceName, a =>
                    a.Location?.LocationType?.Id is (int)ELocationType.iRODS or (int)ELocationType.Storage);
                continue;
            }

            var providerStorage = resolved.PerProvider.GetValueOrDefault(provider.Id);

            AssociateStaging(resource, provider.Id, efpCoreServiceName, providerStorage?.Staging, unspecifiedType);
            AssociateProviderIrods(resource, provider.Id, efpCoreServiceName, providerStorage, gbType);
            AssociateSharedIrods(resource, config, efpCoreServiceName, resolved, providerStorage, gbType);
        }
    }

    private void AssociateStaging(
        ProjectResource resource, Guid providerId, string efpCoreServiceName, Location? staging, AllocationUnitType? unitType)
    {
        if (staging is null)
        {
            DeactivateAssigned(resource, efpCoreServiceName, a =>
                a.Location?.LocationType?.Id == (int)ELocationType.Storage && a.Location?.Provider?.Id == providerId);
            return;
        }

        var existing = FindAssigned(resource, efpCoreServiceName, staging.Id);
        if (existing is null)
        {
            context.Add(CreateAssignedLocation(staging, resource, unitType, value: null, efpCoreServiceName));
        }
        else
        {
            existing.IsDeleted = false;
        }
    }

    private void AssociateProviderIrods(
        ProjectResource resource, Guid providerId, string efpCoreServiceName, ProviderStorage? providerStorage, AllocationUnitType? unitType)
    {
        var irods = providerStorage?.Irods;
        if (irods is null)
        {
            DeactivateAssigned(resource, efpCoreServiceName, a =>
                a.Location?.LocationType?.Id == (int)ELocationType.iRODS && a.Location?.Provider?.Id == providerId);
            return;
        }

        var existing = FindAssigned(resource, efpCoreServiceName, irods.Id);
        if (existing is null)
        {
            context.Add(CreateAssignedLocation(irods, resource, unitType, providerStorage!.IrodsAmount, efpCoreServiceName));
        }
        else
        {
            // Config present + location active ⇒ ensure the association is active with the current
            // amount. (The old exporter set the amount but forgot to undelete here — a latent bug;
            // undeleting matches the staging/shared branches and ADR-0061's "ensure" intent.)
            existing.IsDeleted = false;
            existing.AllocationUnitCount = providerStorage!.IrodsAmount;
        }
    }

    private void AssociateSharedIrods(
        ProjectResource resource,
        StorageConfigPayload config,
        string efpCoreServiceName,
        ResolvedStorage resolved,
        ProviderStorage? providerStorage,
        AllocationUnitType? unitType)
    {
        // Shared iRODS applies when explicitly forced for all entities, or as the fallback for a
        // provider that has no per-provider iRODS mapping of its own.
        var appliesToResource = config.UseSharedIrodsForAllEntities || providerStorage?.Irods is null;
        if (!appliesToResource)
        {
            return;
        }

        if (resolved.SharedIrods is null)
        {
            // Shared iRODS not configured ⇒ deactivate the resource's shared (EFP-Core-provider) iRODS.
            DeactivateAssigned(resource, efpCoreServiceName, a =>
                a.Location?.LocationType?.Id == (int)ELocationType.iRODS
                && a.Location?.Provider?.Id == resolved.EfpCoreProvider.Id);
            return;
        }

        var existing = FindAssigned(resource, efpCoreServiceName, resolved.SharedIrods.Id);
        if (existing is null)
        {
            context.Add(CreateAssignedLocation(resolved.SharedIrods, resource, unitType, resolved.SharedIrodsAmount, efpCoreServiceName));
        }
        else
        {
            existing.IsDeleted = false;
            existing.AllocationUnitCount = resolved.SharedIrodsAmount;
        }
    }

    private async Task<Provider> EnsureEfpCoreProviderAsync(
        StorageConfigPayload config, string efpCoreServiceName, CancellationToken ct)
    {
        var existing = await context.ProvidersQuery
            .FirstOrDefaultAsync(p => p.ServiceName == efpCoreServiceName, ct);

        if (existing is not null)
        {
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
            }

            return existing;
        }

        // Country.Name is unique (UniqueLookupNames), so a second "FI" row would fail the save.
        var country = await context.CountriesQuery.FirstOrDefaultAsync(c => c.Name == "FI", ct)
            ?? new Country { Name = "FI" };

        var provider = new Provider
        {
            Id = Guid.CreateVersion7(),
            Name = efpCoreServiceName,
            Country = country,
            Description = "EFP Core Provider for shared services",
            IsPrivate = false,
            Mode = EOperationMode.ReadOnly,
            ResponsibleUsers = [],
            ServiceName = efpCoreServiceName,
            ExternalId = null,
            // No shared iRODS configured ⇒ the synthetic provider is created soft-deleted.
            IsDeleted = config.SharedIrods is null,
            CreatedAt = DateTime.UtcNow,
        };

        context.Add(provider);
        return provider;
    }

    /// <summary>
    /// Ensure/undelete a <see cref="Location"/> for <paramref name="provider"/> keyed
    /// (Name, LocationType, Provider). Returns the active tracked location.
    /// </summary>
    private async Task<Location> EnsureStorageLocationAsync(
        Provider provider, StorageInstancePayload cfg, ELocationType type, string efpCoreServiceName, CancellationToken ct)
    {
        // Scope to EFP-Core-owned rows (review MEDIUM): B10 is the sole owner of these Locations, so a
        // foreign-service Location that happens to share this provider must never be adopted here.
        var existing = await context.LocationsQuery
            .FirstOrDefaultAsync(
                l => l.Name == cfg.Name && l.LocationTypeId == (int)type && l.ProviderId == provider.Id
                    && l.ServiceName == efpCoreServiceName, ct);

        if (existing is not null)
        {
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
            }

            return existing;
        }

        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            Name = cfg.Name,
            Description = null,
            Endpoint = cfg.Endpoint,
            IsPrivate = false,
            IsShared = true,
            CreatedAt = DateTime.UtcNow,
            DefaultServiceIntegrationUrl = null,
            LocationTypeId = (int)type,
            ProviderId = provider.Id,
            Provider = provider,
            Mode = EOperationMode.ReadOnly,
            ServiceName = efpCoreServiceName,
        };

        context.Add(location);
        return location;
    }

    /// <summary>Soft-deletes every active EFP-Core location of <paramref name="type"/> under a provider; returns null (no active location).</summary>
    private async Task<Location?> DeactivateProviderLocationsOfTypeAsync(
        Guid providerId, ELocationType type, string efpCoreServiceName, CancellationToken ct)
    {
        // Scope to EFP-Core-owned rows (review MEDIUM): a foreign-service Location under the same
        // provider must never be soft-deleted by the reconcile.
        var locations = await context.LocationsQuery
            .Where(l => l.ProviderId == providerId && l.LocationTypeId == (int)type && !l.IsDeleted
                && l.ServiceName == efpCoreServiceName)
            .ToListAsync(ct);

        foreach (var location in locations)
        {
            location.IsDeleted = true;
        }

        return null;
    }

    private async Task DeactivateProviderWithLocationsAsync(Provider provider, string efpCoreServiceName, CancellationToken ct)
    {
        if (!provider.IsDeleted)
        {
            provider.IsDeleted = true;
        }

        // Scope to EFP-Core-owned rows (review MEDIUM): only B10's own Locations under the EFP-Core
        // provider are deactivated.
        var locations = await context.LocationsQuery
            .Where(l => l.ProviderId == provider.Id && !l.IsDeleted && l.ServiceName == efpCoreServiceName)
            .ToListAsync(ct);

        foreach (var location in locations)
        {
            location.IsDeleted = true;
        }
    }

    private async Task<AllocationUnitType?> FindAllocationUnitTypeAsync(string name, CancellationToken ct) =>
        await context.AllocationUnitTypesQuery.FirstOrDefaultAsync(a => a.Name == name, ct);

    private static AssignedLocation CreateAssignedLocation(
        Location location, ProjectResource resource, AllocationUnitType? unitType, int? value, string efpCoreServiceName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            LocationId = location.Id,
            Location = location,
            ProjectResourceId = resource.Id,
            ProjectResource = resource,
            // Legacy parity (review LOW): the old exporter fell back to FK 0 when the unit type was absent.
            AllocationUnitTypeId = unitType?.Id ?? 0,
            AggregationName = null,
            AllocationUnitCount = value,
            Description = null,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
            ServiceName = efpCoreServiceName,
            Mode = EOperationMode.ReadOnly,
            AssignedLocationsValues = [],
            ExternalId = null,
        };

    /// <summary>Finds the resource's EFP-Core assigned location for <paramref name="locationId"/> (scope-safe: never a foreign-service row).</summary>
    private static AssignedLocation? FindAssigned(ProjectResource resource, string efpCoreServiceName, Guid locationId) =>
        resource.AssignedLocations.FirstOrDefault(a => a.ServiceName == efpCoreServiceName && a.LocationId == locationId);

    /// <summary>Soft-deletes the resource's active EFP-Core assigned locations matching <paramref name="predicate"/>.</summary>
    private static void DeactivateAssigned(ProjectResource resource, string efpCoreServiceName, Func<AssignedLocation, bool> predicate)
    {
        foreach (var assigned in resource.AssignedLocations)
        {
            if (!assigned.IsDeleted && assigned.ServiceName == efpCoreServiceName && predicate(assigned))
            {
                assigned.IsDeleted = true;
            }
        }
    }

    private static Dictionary<Guid, StorageInstancePayload> ByProviderGuid(IReadOnlyList<StorageInstancePayload> instances)
    {
        var map = new Dictionary<Guid, StorageInstancePayload>();
        foreach (var instance in instances)
        {
            if (Guid.TryParse(instance.HostingEntityExternalId, out var providerGuid))
            {
                map[providerGuid] = instance;
            }
        }

        return map;
    }

    private sealed record ResolvedStorage(
        Provider EfpCoreProvider,
        Location? SharedIrods,
        int? SharedIrodsAmount,
        IReadOnlyDictionary<Guid, ProviderStorage> PerProvider);

    private sealed record ProviderStorage(Location? Irods, int? IrodsAmount, Location? Staging);
}