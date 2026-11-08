using Core.Domain.Shared;

namespace Core.DataAccess.Queries.Contracts;

public interface ILocationQuery
{
    Task<QueryPage<LocationSummary>> GetLocationsAsync(LocationFilter filter, int page, int limit, CancellationToken ct);

    /// <summary>
    /// Returns a single active location addressed by its (provider, name) natural key, or null when none
    /// matches. Names match case-insensitively via SQL collation. A surrogate id never crosses this boundary
    /// (ADR-0029) — it is resolved and used only inside the read.
    /// <para>
    /// <paramref name="scope"/> is the caller's read-scope tier (ADR-0018): <c>null</c> resolves any active
    /// location (admin / provider identity); a non-null project set is the user tier — the location resolves
    /// only when reachable through one of those projects, otherwise null (identical to "not found", so the
    /// natural key leaks no existence).
    /// </para>
    /// </summary>
    Task<LocationSummary?> GetLocationByProviderAndNameAsync(ProviderName providerName, LocationName locationName, IReadOnlyList<ProjectShortName>? scope, CancellationToken ct);

    /// <summary>
    /// Returns a single active location addressed by its surrogate id, or null when none matches. A surrogate id
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<IList<LocationBaseData>> GetLocationsAsync(CancellationToken ct);

    /// <summary>
    /// Returns whether the LocationType table has a row for <paramref name="locationType"/>, which catches drift
    /// between the domain enum and the seeded reference data.
    /// </summary>
    Task<bool> LocationTypeExistsAsync(ELocationType locationType, CancellationToken ct);
}