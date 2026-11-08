namespace Core.DataAccess.Queries.Contracts;

public interface IRequestQuery
{
    Task<QueryPage<RequestSummary>> GetRequestsAsync(RequestFilter filter, int page, int limit, CancellationToken ct);
    Task<PublicRequestSummary?> GetPublicRequestSummaryAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// The id of the user's pending self-onboarding request on the project, or null when none — serves
    /// both the duplicate guard and reading back the id of a just-created request (a self-onboarding
    /// request is pending only in the <c>Pending</c> state, and at most one exists per project+user).
    /// </summary>
    Task<Guid?> FindPendingSelfOnboardingIdAsync(ProjectId projectId, UserId userId, CancellationToken ct);

    /// <summary>Resolves an action-link token to its request and action, or null when unknown/handled.</summary>
    Task<ActionLinkTarget?> FindActionLinkTargetAsync(string token, CancellationToken ct);

    /// <summary>
    /// The surrogate id of the request bearing this v2 <paramref name="slug"/>, or null when none has it —
    /// which includes every v1-created row, since those carry a null slug and so are unaddressable by slug
    /// (ADR-0024). Lets the authenticated handle endpoints address a request by its GUID-free public id.
    /// </summary>
    Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken ct);
}