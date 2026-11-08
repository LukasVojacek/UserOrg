using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

/// <summary>
/// Read-side queries over the <see cref="OnboardingRequest"/> aggregate. The aggregate has no CLR
/// navigation to Project/User (both are one-way shadow FKs), so the per-project / per-requester filters
/// and the projected natural keys are matched via explicit joins over the Project/User sets keyed by
/// their shadow PKs — NOT nested correlated <c>.Any()</c>, which the EF in-memory provider mistranslates
/// for multi-set shadow-FK queries (repo memory).
/// </summary>
internal sealed class OnboardingRequestQuery(
    IDataQuery<OnboardingRequest> onboardingQuery) : IOnboardingRequestQuery
{
    public Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken ct)
    {
        // A structurally invalid slug cannot match any stored row, so it is answered as a plain miss rather
        // than as an error: the callers turn a null into the same not_found they use for an absent slug, and
        // a distinct outcome here would tell an enumerator that a slug is well-formed (ADR-0020, ADR-0018 rider).
        if (!OnboardingRequestSlug.TryCreate(slug, out var parsed, out _))
            return Task.FromResult<Guid?>(null);

        return onboardingQuery.FirstOrDefaultAsync(
            q => q
                .Where(r => r.Slug == parsed)
                .Select(r => (Guid?)EF.Property<Guid>(r, "Id")),
            "FindOnboardingRequestIdBySlug",
            ct);
    }

    public Task<Guid?> FindPendingIdAsync(ProjectId projectId, UserId userId, CancellationToken ct)
        => onboardingQuery.FirstOrDefaultAsync(
            q => q
                .Where(r => EF.Property<ProjectId>(r, "ProjectId") == projectId
                    && EF.Property<UserId>(r, "UserId") == userId
                    && r.State == OnboardingRequestState.Pending)
                .Select(r => (Guid?)EF.Property<Guid>(r, "Id")),
            "FindPendingOnboardingRequestId",
            ct);

    public Task<OnboardingRequestSettlement?> FindSettlementBySlugAsync(string slug, CancellationToken ct)
    {
        // Same rule as FindIdBySlugAsync: an unparseable slug is a miss, not an error.
        if (!OnboardingRequestSlug.TryCreate(slug, out var parsed, out _))
            return Task.FromResult<OnboardingRequestSettlement?>(null);

        return onboardingQuery.FirstOrDefaultAsync<Domain.User.User, Domain.Project.Project, OnboardingRequestSettlement>(
            (requests, users, projects) => requests
                .Where(r => r.Slug == parsed)
                .Join(
                    projects,
                    r => EF.Property<ProjectId>(r, "ProjectId"),
                    p => EF.Property<ProjectId>(p, "Id"),
                    (r, p) => new { Request = r, Project = p })
                .Join(
                    users,
                    rp => EF.Property<UserId>(rp.Request, "UserId"),
                    u => EF.Property<UserId>(u, "Id"),
                    (rp, u) => new OnboardingRequestSettlement(
                        rp.Project.ShortName,
                        u.IdentityProviderId,
                        rp.Request.State)),
            "FindOnboardingRequestSettlementBySlug",
            ct);
    }

    public Task<QueryPage<OnboardingRequestSummary>> GetForCallerAsync(OnboardingRequestListFilter filter, int page, int limit, CancellationToken ct)
    {
        var managed = filter.ManagedProjects;
        if (filter.Requester is null && managed.Count == 0)
            return Task.FromResult(new QueryPage<OnboardingRequestSummary>([], 0));

        return onboardingQuery.ToPagedListAsync<Domain.User.User, Domain.Project.Project, OnboardingRequestSummary>(
            (requests, users, projects) =>
            {
                var joined = InState(requests, filter.State)
                    .Join(
                        Narrowed(projects, filter.Project),
                        r => EF.Property<ProjectId>(r, "ProjectId"),
                        p => EF.Property<ProjectId>(p, "Id"),
                        (r, p) => new { Request = r, Project = p })
                    .Join(
                        users,
                        rp => EF.Property<UserId>(rp.Request, "UserId"),
                        u => EF.Property<UserId>(u, "Id"),
                        (rp, u) => new { rp.Request, rp.Project, User = u });

                // why: the halves are chosen here, before EF sees the expression, so a dropped sent half never puts a null Requester into parameter extraction
                joined = filter.Requester switch
                {
                    { } requester when managed.Count > 0 => joined.Where(x => x.User.IdentityProviderId == requester || managed.Contains(x.Project.ShortName)),
                    { } requester => joined.Where(x => x.User.IdentityProviderId == requester),
                    null => joined.Where(x => managed.Contains(x.Project.ShortName)),
                };

                return joined
                    .OrderBy(x => EF.Property<Guid>(x.Request, "Id"))
                    .Select(x => new OnboardingRequestSummary(
                        x.Request.Slug,
                        x.Request.State,
                        x.Project.ShortName,
                        x.User.IdentityProviderId,
                        x.Request.ContactEmail,
                        managed.Contains(x.Project.ShortName) ? OnboardingRequestDirection.Received : OnboardingRequestDirection.Sent));
            },
            page,
            limit,
            "GetOnboardingRequestsForCaller",
            ct);
    }

    private static IQueryable<OnboardingRequest> InState(IQueryable<OnboardingRequest> requests, int? state)
    {
        if (state is not { } s)
            return requests;

        var requestState = OnboardingRequestState.Create(s);
        return requests.Where(r => r.State == requestState);
    }

    private static IQueryable<Domain.Project.Project> Narrowed(IQueryable<Domain.Project.Project> projects, ProjectShortName? project) =>
        project is { } shortName ? projects.Where(p => p.ShortName == shortName) : projects;
}