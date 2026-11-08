namespace Core.DataAccess.Queries.Contracts;

public interface IOnboardingRequestQuery
{
    /// <summary>
    /// The surrogate id of the onboarding request bearing this <paramref name="slug"/>, or null when none —
    /// soft-deleted rows are excluded by the query filter. Lets the authenticated accept/reject endpoints
    /// address a request by its GUID-free public id (ADR-0024).
    /// </summary>
    Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// The id of the requester's pending onboarding request on the project, or null when none — serves the
    /// duplicate-pending guard. At most one pending request exists per project+requester.
    /// </summary>
    Task<Guid?> FindPendingIdAsync(ProjectId projectId, UserId userId, CancellationToken ct);

    /// <summary>
    /// The settlement context (route project, requester IdpId, current state) of the onboarding request
    /// bearing this <paramref name="slug"/>, or null when none — soft-deleted rows are excluded by the query
    /// filter. The accept/reject endpoints use the resolved <c>Project</c> to enforce the ADR-0018 anti-leak
    /// assertion (the request must belong to the route project) since the loaded aggregate cannot expose its
    /// natural keys (they are EF-ignored shadow FKs).
    /// </summary>
    Task<OnboardingRequestSettlement?> FindSettlementBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// The caller's sent and received requests (paged) per <paramref name="filter"/>, ordered by surrogate id.
    /// A request matching both halves appears once, as <see cref="OnboardingRequestDirection.Received"/>.
    /// Items carry the Slug.
    /// </summary>
    Task<QueryPage<OnboardingRequestSummary>> GetForCallerAsync(OnboardingRequestListFilter filter, int page, int limit, CancellationToken ct);
}