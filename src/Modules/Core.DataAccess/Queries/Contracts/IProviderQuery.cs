namespace Core.DataAccess.Queries.Contracts;

public interface IProviderQuery
{
    Task<QueryPage<ProviderSummary>> GetProvidersAsync(ProviderFilter filter, int page, int limit, CancellationToken ct);

    /// <summary>
    /// Returns a single provider with its location headers, or null if no active provider has the name.
    /// Projects only the columns the read needs — unlike loading the write aggregate, it does not pull
    /// the location allocations and assigned values.
    /// <para>
    /// <paramref name="scope"/> is the caller's read-scope tier (ADR-0018): <c>null</c> resolves any active
    /// provider (admin / provider identity); a non-null project set is the user tier — a private provider
    /// resolves only when reachable through one of those projects, otherwise null (identical to "not found",
    /// so existence never leaks). Public providers resolve for everyone.
    /// </para>
    /// </summary>
    Task<ProviderDetail?> GetProviderDetailsAsync(ProviderName name, IReadOnlyList<ProjectShortName>? scope, CancellationToken ct);

    /// <summary>
    /// One page of administered providers with their locations, ordered by name. An empty
    /// <see cref="AdministeredProviderFilter.Names"/> set returns an empty page without querying.
    /// </summary>
    Task<QueryPage<ProviderDetail>> GetAdministeredProvidersAsync(AdministeredProviderFilter filter, int page, int limit, CancellationToken ct);

    /// <summary>
    /// Whether a provider with the given name exists, including soft-deleted (deactivated) ones.
    /// Bypasses the global IsDeleted query filter so create-time uniqueness covers deactivated rows.
    /// </summary>
    Task<bool> ProviderExistsAsync(ProviderName name, CancellationToken ct);

    /// <summary>
    /// Resolves an active provider's surrogate <see cref="ProviderId"/> from its natural key, or null when no
    /// active provider has the name. The surrogate is an EF shadow id that never crosses a contract; this lets a
    /// write use-case (e.g. promote-to-provider-admin) pass the resolved id into a synchronous aggregate method
    /// without exposing it on the domain surface (ADR-0029 §3/§4).
    /// </summary>
    Task<ProviderId?> FindActiveProviderIdByNameAsync(ProviderName name, CancellationToken ct);
}