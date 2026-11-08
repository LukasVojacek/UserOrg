namespace Core.DataAccess.Queries.Contracts;

public interface IResourceQuery
{
    Task<QueryPage<ResourceSummary>> GetResourcesAsync(ResourceFilter filter, int page, int limit, CancellationToken ct);
    Task<ResourceSummary?> GetResourceAsync(Guid id, IReadOnlyList<ProjectShortName> scope, CancellationToken ct);

    /// <summary>
    /// Resolves a single resource by its natural key (name) within the caller's read scope (ADR-0022).
    /// Returns null both when no resource has the name and when it exists but is outside <paramref name="scope"/>,
    /// so the caller emits the identical 404 for missing and out-of-scope (ADR-0018 rider).
    /// </summary>
    Task<ResourceSummary?> GetResourceByNameAsync(ResourceName name, IReadOnlyList<ProjectShortName> scope, CancellationToken ct);

    /// <summary>
    /// Resolves a single resource by its full natural key <c>(providerName, resourceName)</c> within the
    /// caller's read scope (ADR-0022). A resource name is unique only per provider, so the provider qualifies
    /// it: the row is returned only when it has an active assignment on a location of <paramref name="providerName"/>.
    /// Returns null for missing, wrong-provider, and out-of-scope alike, so the caller emits the identical 404
    /// (ADR-0018 rider).
    /// <para>
    /// <paramref name="scope"/> is the caller's read-scope tier (ADR-0018): <c>null</c> resolves any active
    /// resource (admin / provider identity); a non-null project set is the user tier (assigned to one of those
    /// projects). An empty set resolves nothing.
    /// </para>
    /// </summary>
    Task<ResourceSummary?> GetResourceByProviderAndNameAsync(ProviderName providerName, ResourceName name, IReadOnlyList<ProjectShortName>? scope, CancellationToken ct);

    /// <summary>
    /// Whether an active resource already carries <paramref name="name"/> under the active provider
    /// <paramref name="providerName"/> (per-provider uniqueness, ADR-0026/0031). The create use-case loads the
    /// owning <c>Provider</c> for the FK separately (the 404-on-missing-provider path), so this is a NameTaken-only
    /// pre-check for the 409; the filtered unique index is the concurrency backstop. Soft-deleted rows never count.
    /// </summary>
    Task<bool> IsResourceNameTakenAsync(ProviderName providerName, ResourceName name, CancellationToken ct);
}