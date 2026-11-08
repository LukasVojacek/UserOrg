namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Write-validation reads for the resource-assignment endpoints (assign / update / remove an
/// <c>AssignedLocation</c>). Deliberately separate from <see cref="IResourceQuery"/>, which is the
/// caller-scoped read model (a resource is visible only inside the caller's project scope): these
/// lookups are authority-scoped pre-write checks for an Admin/ProviderAdmin actor, not caller-scoped.
/// The resource itself is loaded by its natural key through the repository (ADR-0044 L2.4); these reads
/// answer the cross-aggregate questions the loaded aggregate cannot.
/// </summary>
public interface IResourceAssignmentQuery
{
    /// <summary>
    /// Cross-aggregate invariant #1: whether an ACTIVE assignment of the resource identified by
    /// <paramref name="key"/> already sits on a non-shared location. Soft-deleted allocations and shared
    /// locations do not count. Resolves the surrogate from the natural key internally (ADR-0044 L2.6).
    /// </summary>
    Task<bool> HasNonSharedLocationAsync(ProjectResourceKey key, CancellationToken ct);

    /// <summary>Whether the seeded <c>AllocationUnitType</c> lookup row exists (assign + update → 400 if not).</summary>
    Task<bool> AllocationUnitTypeExistsAsync(int allocationUnitTypeId, CancellationToken ct);

    /// <summary>
    /// The projects currently using the resource, each paired with the resource name — one entry per
    /// HEAppE <c>WorkItem</c> the post-commit sync handler enqueues (S3). Excludes soft-deleted links.
    /// Resolves the surrogate from the natural key internally (ADR-0044 L2.6).
    /// </summary>
    Task<IReadOnlyList<ResourceProjectRef>> GetProjectsUsingResourceAsync(ProjectResourceKey key, CancellationToken ct);

    /// <summary>
    /// Resolves the resource's surrogate <c>Guid</c> from its natural key, for the output-cache eviction tag
    /// only (the templates endpoint tags responses by the surrogate, ADR-0016). The <c>Guid</c> is a pure
    /// storage detail returned here so the eviction handler can rebuild the exact <c>resource:{guid}</c> tag;
    /// it never re-enters domain or other contract surfaces (ADR-0029 / 0041). Returns null when no active
    /// resource matches the key.
    /// </summary>
    Task<Guid?> GetResourceSurrogateAsync(ProjectResourceKey key, CancellationToken ct);
}