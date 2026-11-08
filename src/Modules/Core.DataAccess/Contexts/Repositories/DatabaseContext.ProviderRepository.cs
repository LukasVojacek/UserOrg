using Core.DataAccess.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

internal sealed partial class DatabaseContext : IProviderRepository
{
    // Lean load shape: the provider, its locations and their allocations — but NOT the allocations' resources.
    // This is the general read/load path used by ~all provider/location endpoints, which never touch the
    // allocated resources.
    private IQueryable<Domain.Provider.Provider> _providerIncludes => Providers
        .AsSplitQuery()
        .Include("_providerLocations._locationAllocations._assignedLocationValues");

    // Adds the allocated resource (+ its owning provider) so an AssignedLocation exposes its natural key and
    // operation mode in-memory for the cross-aggregate read-only guard (ADR-0044 L2.1/L2.5). Used ONLY by the
    // assignment-mutating loads (update/remove-assignment); kept off the lean path so unrelated provider reads
    // do not eager-load every allocation's resource.
    private IQueryable<Domain.Provider.Provider> _providerIncludesWithResources => _providerIncludes
        .Include("_providerLocations._locationAllocations._resource._provider");

    async ValueTask<Domain.Provider.Provider?> IProviderRepository.FindIncludingDeactivatedAsync(ProviderName key, CancellationToken cancellationToken) => await _providerIncludes
        // Ignore only the provider's own soft-delete filter; child Location/AssignedLocation filters stay
        // active so a deactivated provider does not come back with soft-deleted children attached.
        .IgnoreQueryFilters(["ProviderActive"])
        .Where(p => p.Name == key)
        .OrderByTwinResolution()
        .FirstOrDefaultAsync(cancellationToken);

    async ValueTask<Domain.Provider.Provider?> IProviderRepository.FindWithAllocatedResourcesAsync(ProviderName key, CancellationToken cancellationToken) => await _providerIncludesWithResources
        .FirstOrDefaultAsync(p => p.Name == key, cancellationToken);

    // See IProviderRepository.MarkAssignedLocationAsAdded for why this is necessary: AssignedLocation.Id is
    // client-generated, so a new row discovered through an already-tracked parent Location's collection comes
    // out of DetectChanges as Modified rather than Added. `Entry(x).State = Added` tracks only x — it does not
    // traverse navigations — so the still-untracked AssignedLocationValue children never get attached and never
    // get INSERTed. `Add(x)` walks the whole reachable graph instead: every UNTRACKED node (the
    // AssignedLocationValue children) is marked Added regardless of key, while already-tracked nodes (the new
    // ProjectResource, already Added via its own repository; the provider, tracked Unchanged) are left exactly
    // as they are.
    void IProviderRepository.MarkAssignedLocationAsAdded(Domain.Provider.AssignedLocation assignedLocation) =>
        Add(assignedLocation);

    async ValueTask<Domain.Provider.Provider?> IRepository<Domain.Provider.Provider, ProviderName>.AddAsync(Domain.Provider.Provider aggregate, CancellationToken cancellationToken)
    {
        // Provider.Id is database-generated (NewId()); do not assign it here.
        await Providers.AddAsync(aggregate, cancellationToken);
        RegisterAddedAggregateRoot(aggregate);
        return await Task.FromResult(aggregate);
    }

    void IRepository<Domain.Provider.Provider, ProviderName>.Delete(Domain.Provider.Provider aggregate, CancellationToken cancellationToken) => Providers.Remove(aggregate);

    async ValueTask<Domain.Provider.Provider?> IRepository<Domain.Provider.Provider, ProviderName>.FindAsync(ProviderName key, CancellationToken cancellationToken) => await _providerIncludes
        .FirstOrDefaultAsync(p => p.Name == key, cancellationToken);
}