using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Http;

namespace Waldur.Sync;

/// <summary>
/// Typed HTTP client over the six Waldur endpoints the sync pull uses. Returns the raw API DTOs.
/// Detail fetches <b>throw</b> (<see cref="Http.WaldurApiException"/>) on any page failure — a partial
/// detail list is never presented as complete (D7 / ADR-0056/A4). The two <b>list</b> fetches
/// (customers, projects) are resilient (ADR-0064): they return a <see cref="PagedFetch{T}"/> whose
/// <see cref="PagedFetch{T}.IsComplete"/> is false when a page failed, so the caller can suppress the
/// absence sweep for that entity type instead of aborting the whole iteration.
/// </summary>
public interface IWaldurApiClient
{
    /// <summary><c>GET /api/customers/</c> — resilient: a failed page yields a partial, incomplete result.</summary>
    Task<PagedFetch<ApiCustomer>> GetCustomersAsync(CancellationToken ct);

    /// <summary><c>GET /api/marketplace-public-offerings/?customer_uuid=…</c></summary>
    Task<IReadOnlyList<ApiPublicOffering>> GetCustomerOfferingsAsync(Guid customerUuid, CancellationToken ct);

    /// <summary><c>GET /api/projects/</c> — resilient: a failed page yields a partial, incomplete result.</summary>
    Task<PagedFetch<ApiProject>> GetProjectsAsync(CancellationToken ct);

    /// <summary><c>GET /api/marketplace-resources/?project_uuid=…</c></summary>
    Task<IReadOnlyList<ApiMarketplaceResource>> GetProjectResourcesAsync(Guid projectUuid, CancellationToken ct);

    /// <summary><c>GET /api/marketplace-public-offerings/{uuid}/</c></summary>
    Task<ApiPublicOfferingDetail> GetOfferingDetailAsync(Guid offeringUuid, CancellationToken ct);

    /// <summary><c>GET /api/projects/{uuid}/list_users/</c></summary>
    Task<IReadOnlyList<ApiProjectUser>> GetProjectUsersAsync(Guid projectUuid, CancellationToken ct);
}