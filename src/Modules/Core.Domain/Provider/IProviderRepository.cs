namespace Core.Domain.Provider;

/// <summary>
/// Write port for the Provider aggregate. Extends the generic repository with a load that bypasses the
/// soft-delete query filter, because <see cref="IRepository{TAggregate,TKey}.FindAsync"/> filters out
/// deactivated providers and Reactivate (and an idempotent Deactivate) must still find them.
/// </summary>
public interface IProviderRepository : IRepository<Provider, ProviderName>
{
    /// <summary>
    /// Loads the provider by name, deactivated or not. When legacy name twins share the name, returns the first by
    /// the twin resolution order: the active row, then <c>ModifiedAt</c> descending (NULL last), then
    /// <c>CreatedAt</c> descending, then the surrogate id.
    /// </summary>
    ValueTask<Provider?> FindIncludingDeactivatedAsync(ProviderName key, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the provider with every allocation's owning <c>ProjectResource</c> (and its provider) materialised,
    /// so an <c>AssignedLocation</c> can expose its resource's natural key and operation mode in-memory
    /// (ADR-0044 L2.1/L2.5). Used only by the assignment-mutating loads (update/remove-assignment) that read
    /// <c>ResourceOperationMode</c>/<c>ResourceKey</c> off existing allocations; plain provider reads use the
    /// lean <see cref="IRepository{TAggregate,TKey}.FindAsync"/> and do not eager-load resources.
    /// </summary>
    ValueTask<Provider?> FindWithAllocatedResourcesAsync(ProviderName key, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a brand new <see cref="AssignedLocation"/> — one just added to a <see cref="Location"/> collection
    /// of an already-tracked <see cref="Provider"/> aggregate (e.g. one reloaded via <see cref="FindAsync"/> in
    /// the caller's own scope) — as a row to INSERT, together with its still-untracked
    /// <c>AssignedLocationValue</c> children.
    /// Necessary only because <c>AssignedLocation.Id</c> is client-generated: when such a row is discovered by
    /// EF's change detection through a tracked parent's collection rather than attached directly, the
    /// already-set key makes the NavigationFixer's graph-attach heuristic mark it Modified instead of Added,
    /// and SaveChanges then emits an UPDATE that matches no row. The caller must call this for every
    /// <see cref="AssignedLocation"/> it knows to be new before saving.
    /// </summary>
    void MarkAssignedLocationAsAdded(AssignedLocation assignedLocation);
}