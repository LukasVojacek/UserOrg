using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Provider;

/// <summary>
/// Pure domain service that creates a <see cref="ProjectResource"/> under a provider and allocates it to one or
/// more of the provider's locations as a single in-memory operation (ADR-0043, ADR-0050). Cross-aggregate
/// creation belongs in a domain service, not on <see cref="Provider"/>, which must not manufacture and return
/// another aggregate root.
/// </summary>
public interface IResourceProvisioningService
{
    /// <summary>
    /// Provisions a new <see cref="ProjectResource"/> for <paramref name="provider"/> and allocates it to every
    /// location in <paramref name="assignments"/>, in order, as one cohesive, in-memory operation (ADR-0050).
    /// Pure — no I/O, synchronous. All-or-nothing: the first failing entry (unknown/read-only location, or the
    /// resource already holding a non-shared location — including one assigned earlier in this same batch)
    /// aborts the whole call, so no half-built (partially-assigned) resource can arise. Returns the new resource
    /// for the application to persist in a single transaction; the returned aggregate already carries a
    /// <see cref="ProjectResourceActivatedEvent"/>, dispatched by the unit of work once it is saved. Returns only
    /// domain errors (the application owns provider/location resolution and the 404s).
    /// </summary>
    /// <param name="provider">The resolved owning aggregate to operate on (never a surrogate id).</param>
    /// <param name="resource">The new resource's own data.</param>
    /// <param name="createdBy">The user creating the resource.</param>
    /// <param name="assignments">The ordered, non-empty list of locations (natural keys, resolved inside
    /// <paramref name="provider"/>) and their per-location allocation data.</param>
    Result<ProjectResource, Error> Provision(
        Provider provider,
        NewResourceSpec resource,
        UserId createdBy,
        IReadOnlyList<LocationAssignmentSpec> assignments);
}