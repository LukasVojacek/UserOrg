using Core.Domain.Primitives;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Read-side lookups over the Core-owned resource-request projection (ADR-0063). Served from the
/// schema-isolated <c>resourceRequests</c> unit, independent of the modular <c>DatabaseContext</c>.
/// </summary>
public interface IProjectResourceRequestQuery
{
    /// <summary>Per-project list (paged), optionally filtered by state name. Items carry the slug and timestamps.</summary>
    Task<QueryPage<ResourceRequestSummary>> ListByProjectAsync(string projectShortName, string? state, int page, int limit, CancellationToken ct);

    /// <summary>Full detail of the request bearing this slug, or null when none.</summary>
    Task<ResourceRequestDetail?> FindBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// The request whose stored token hash equals <paramref name="tokenHash"/> and whose token has not expired
    /// as of <paramref name="asOf"/>, or null — the anonymous action-link authority check (ADR-0064).
    /// </summary>
    Task<ResourceRequestDetail?> FindByValidTokenHashAsync(string tokenHash, DateTimeOffset asOf, CancellationToken ct);

    /// <summary>
    /// Every request parked in <c>AwaitingResourceCreation</c> for the given resource key (provider + resource),
    /// across all projects — resumed when that resource is created (DEC-4).
    /// </summary>
    Task<IReadOnlyList<ResourceRequestDetail>> FindAwaitingByResourceKeyAsync(string providerName, string resourceName, CancellationToken ct);

    /// <summary>
    /// The request for the given resource key (provider + resource) in any state, or null if none exists.
    /// </summary>
    /// <param name="providerName"></param>
    /// <param name="resourceName"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<ResourceRequestDetail?> FindByResourceKeyAsync(string providerName, string resourceName, CancellationToken ct);

    /// <summary>
    /// Every existing slug that could collide with a new request on <paramref name="baseSlug"/> — the bare base
    /// itself and any string beginning <c>{baseSlug}-</c> — regardless of provider or state. Feeds
    /// <see cref="ResourceRequestSlug.NextAvailable"/> so the endpoint picks the lowest genuinely-free slug rather
    /// than an ordinal derived from a naive count (which mis-attributes a distinct resource's base, ADR-0066).
    /// </summary>
    Task<IReadOnlyList<string>> ListSlugsByBaseAsync(string baseSlug, CancellationToken ct);

    /// <summary>Returns true if a non-terminal request exists for the project/provider/resource key, enforcing single-active invariant.</summary>
    Task<bool> HasActiveRequestAsync(string projectShortName, string providerName, string resourceName, CancellationToken ct);

    /// <summary>
    /// Every non-terminal request (either gate) whose <c>UpdatedAt</c> is at or before <paramref name="cutoff"/> —
    /// returns rows currently due for expiry (not rows already in <c>Expired</c> state), fed to the expiry sweeper
    /// (DEC-7). The <paramref name="cutoff"/> is computed as <c>now - DurationInDays</c>: a past timestamp that
    /// marks the age boundary. Each swept request is resumed with a cancel signal landing it on <c>Expired</c>.
    /// </summary>
    Task<IReadOnlyList<ResourceRequestDetail>> FindDueForExpiryAsync(DateTimeOffset cutoff, CancellationToken ct);
}

/// <summary>List-row projection of a resource request.</summary>
public sealed record ResourceRequestSummary(
    string Slug,
    string State,
    string ProjectShortName,
    string ProviderName,
    string ResourceName,
    string RequesterIdpId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Detail projection of a resource request (slug / token / awaiting lookups).</summary>
public sealed record ResourceRequestDetail(
    string Slug,
    string State,
    string ProjectShortName,
    string ProviderName,
    string ResourceName,
    string RequesterIdpId,
    string ContactEmail,
    Guid WorkflowRequestId,
    string? Reason,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);