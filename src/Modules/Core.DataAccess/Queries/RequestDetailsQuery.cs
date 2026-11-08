using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

internal sealed class RequestDetailsQuery(
    IDataQuery<Domain.Request.Request> requestQuery,
    IDataQuery<ProjectResource> projectResourceQuery,
    IDataQuery<Domain.Provider.Provider> providerQuery,
    IDataQuery<ActionLink> actionLinkQuery) : IRequestQuery
{
    public async Task<QueryPage<RequestSummary>> GetRequestsAsync(RequestFilter filter, int page, int limit, CancellationToken ct)
    {
        // Request has no CLR navigation to Project/User (both relationships are mapped one-way as
        // unnamed FKs), so caller scope, the project/user filters and sender identity are matched via
        // correlated EXISTS subqueries over the Project/User sets, and the natural keys (ShortName,
        // IdpId) are projected via correlated scalar subqueries over the same sets — only Provider has
        // no slot left in the two-extra-entity overload, so its name is stitched in a second pass below.
        var pageRows = await requestQuery.ToPagedListAsync<Domain.User.User, Domain.Project.Project, RequestRow>(
            (requests, users, projects) =>
                // ProjectId is not unique (a project can hold many requests), so end the ORDER BY on the
                // surrogate Id — otherwise rows shuffle across pages. Order by the ProjectId value object directly
                // (it is IComparable for the in-memory provider); EF translates this to ORDER BY the mapped Guid
                // column. Ordering by `r.ProjectId.Value` is NOT SQL-translatable and threw at runtime.
                ApplyFilter(requests, users, projects, filter)
                    .OrderBy(r => r.ProjectId)
                    .ThenBy(r => EF.Property<Guid>(r, "Id"))
                    .Select(r => new RequestRow(
                        r.ContactEmail,
                        r.Type,
                        r.State,
                        // IgnoreQueryFilters: a request on a soft-deleted project/user still resolves its
                        // historical name (the old projection emitted the raw FK regardless of soft-delete).
                        // FirstOrDefault degrades a dangling FK to an empty key instead of 500-ing the page.
                        projects.IgnoreQueryFilters().Where(p => EF.Property<ProjectId>(p, "Id") == r.ProjectId).Select(p => p.ShortName).FirstOrDefault(),
                        users.IgnoreQueryFilters().Where(u => EF.Property<UserId>(u, "Id") == r.UserId).Select(u => u.IdentityProviderId).FirstOrDefault(),
                        r.ProviderId,
                        r.ProjectResourceName,
                        r.Slug)),
            page,
            limit,
            "GetRequests",
            ct);

        var providerNamesById = await LoadProviderNamesAsync(pageRows.Items, ct);
        var items = pageRows.Items.Select(r => ToSummary(r, providerNamesById)).ToList();

        return new QueryPage<RequestSummary>(items, pageRows.TotalItems);
    }

    private static IQueryable<Domain.Request.Request> ApplyFilter(
        IQueryable<Domain.Request.Request> requests,
        IQueryable<Domain.User.User> users,
        IQueryable<Domain.Project.Project> projects,
        RequestFilter filter)
    {
        var scope = filter.ScopeShortNames;
        var shortName = filter.ProjectShortName ?? default;
        var userIdp = filter.UserIdpId ?? default;
        var sender = filter.SenderIdpId ?? default;
        var requestState = filter.State is { } state ? RequestState.Create(state) : default;

        return requests
            .WhereIf(scope is not null, r => projects.Any(p =>
                EF.Property<ProjectId>(p, "Id") == r.ProjectId && scope!.Contains(p.ShortName)))
            .WhereIf(filter.ProjectShortName is not null, r => projects.Any(p =>
                EF.Property<ProjectId>(p, "Id") == r.ProjectId && p.ShortName == shortName))
            .WhereIf(filter.UserIdpId is not null, r => users.Any(u =>
                EF.Property<UserId>(u, "Id") == r.UserId && u.IdentityProviderId == userIdp))
            .WhereIf(filter.SenderIdpId is not null, r => users.Any(u =>
                EF.Property<UserId>(u, "Id") == r.UserId && u.IdentityProviderId == sender))
            .WhereIf(filter.State is not null, r => r.State == requestState);
    }

    private async Task<Dictionary<ProviderId, ProviderName>> LoadProviderNamesAsync(IReadOnlyList<RequestRow> rows, CancellationToken ct)
    {
        // Second pass: resolve provider names for the page's distinct provider ids (the Provider set has
        // no slot in the paged overload). Mirrors the two-pass stitch in GetProjectUsersAsync.
        var providerIds = rows
            .Where(r => r.ProviderId is not null)
            .Select(r => r.ProviderId!.Value)
            .Distinct()
            .ToList();

        return providerIds.Count == 0
            ? []
            : (await providerQuery.ToListAsync(
                q => q.IgnoreQueryFilters()
                    .Where(pv => providerIds.Contains(EF.Property<ProviderId>(pv, "Id")))
                    .Select(pv => new ProviderNameRow(EF.Property<ProviderId>(pv, "Id"), pv.Name)),
                "GetRequestsProviderNames",
                ct)).ToDictionary(p => p.Id, p => p.Name);
    }

    private static RequestSummary ToSummary(RequestRow r, Dictionary<ProviderId, ProviderName> providerNamesById) =>
        new(
            r.ContactEmail,
            r.Type,
            r.State,
            r.ProjectShortName,
            r.IdpUserId,
            r.ProviderId is { } pid && providerNamesById.TryGetValue(pid, out var name) ? name : null,
            r.ProjectResourceName,
            r.Slug);

    private sealed record RequestRow(
        Email? ContactEmail,
        RequestType Type,
        RequestState State,
        ProjectShortName ProjectShortName,
        IdpId IdpUserId,
        ProviderId? ProviderId,
        ResourceName? ProjectResourceName,
        string? Slug);

    private sealed record ProviderNameRow(ProviderId Id, ProviderName Name);

    public async Task<PublicRequestSummary?> GetPublicRequestSummaryAsync(Guid requestId, CancellationToken ct)
    {
        // Request has no CLR navigation to User or Project (both relationships are mapped one-way as
        // unnamed FKs), so join those sets explicitly via their shadow PKs to read requestor + project.
        var row = await requestQuery.FirstOrDefaultAsync<Domain.User.User, Domain.Project.Project, PublicRequestRow>(
            (requests, users, projects) =>
                // Query syntax: three sets joined over shadow FKs with the by-id guard(s) interleaved between the
                // joins. The compiler's transparent identifiers keep every earlier range variable in scope; the same
                // chain as .Join() needs a hand-written anonymous carrier per step. Same expression tree either way
                // (module-conventions.md § LINQ syntax in read queries).
                from r in requests
                where EF.Property<Guid>(r, "Id") == requestId
                join u in users on r.UserId equals EF.Property<UserId>(u, "Id")
                join p in projects on r.ProjectId equals EF.Property<ProjectId>(p, "Id")
                select new PublicRequestRow(
                    u.UserName.Value,
                    u.Email.Value,
                    p.ShortName,
                    r.ProjectResourceId,
                    r.ProviderId,
                    r.State.Value,
                    r.Type.Value,
                    r.Slug),
            "GetPublicRequestSummary",
            ct);

        if (row is null)
            return null;

        string? resourceName = row.ProjectResourceId is { } prId
            ? await projectResourceQuery.FirstOrDefaultAsync(
                q => q.IgnoreQueryFilters()
                    .Where(pr => EF.Property<ProjectResourceId>(pr, "Id") == prId)
                    .Select(pr => pr.ResourceName.Value),
                "GetPublicRequestResourceName",
                ct)
            : null;

        string? providerName = row.ProviderId is { } pvId
            ? await providerQuery.FirstOrDefaultAsync(
                q => q.IgnoreQueryFilters()
                    .Where(pv => EF.Property<ProviderId>(pv, "Id") == pvId)
                    .Select(pv => pv.Name.Value),
                "GetPublicRequestProviderName",
                ct)
            : null;

        var links = await actionLinkQuery.ToListAsync(
            q => q
                .Where(al => EF.Property<Guid>(al, "RequestId") == requestId && !al.IsDeleted)
                .Select(al => new { al.Action, al.Link, al.ValidTo }),
            "GetPublicRequestActionLinks",
            ct);

        var acceptLink = links.FirstOrDefault(al => al.Action == 1)?.Link;
        var rejectLink = links.FirstOrDefault(al => al.Action == 2)?.Link;
        var validTo = links.Count > 0 ? links.Max(al => al.ValidTo) : DateTimeOffset.MinValue;

        return new PublicRequestSummary(
            requestId,
            row.RequestorUserName,
            row.RequestorEmail,
            row.ProjectShortName,
            resourceName,
            providerName,
            row.RequestState,
            validTo,
            row.RequestType,
            acceptLink,
            rejectLink,
            row.Slug);
    }

    public Task<Guid?> FindPendingSelfOnboardingIdAsync(ProjectId projectId, UserId userId, CancellationToken ct)
        => requestQuery.FirstOrDefaultAsync(
            q => q
                .Where(r => r.ProjectId == projectId
                    && r.UserId == userId
                    && r.Type == RequestType.SelfOnboarding
                    && r.State == RequestState.Pending)
                .Select(r => (Guid?)EF.Property<Guid>(r, "Id")),
            "FindPendingSelfOnboardingId",
            ct);

    public Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken ct)
        => requestQuery.FirstOrDefaultAsync(
            // The !IsDeleted query filter applies, so a soft-deleted request resolves to null — consistent
            // with the keyed repository the handler then loads from. v1 rows (null Slug) never match.
            q => q
                .Where(r => r.Slug == slug)
                .Select(r => (Guid?)EF.Property<Guid>(r, "Id")),
            "FindRequestIdBySlug",
            ct);

    public Task<ActionLinkTarget?> FindActionLinkTargetAsync(string token, CancellationToken ct)
        => actionLinkQuery.FirstOrDefaultAsync(
            q => q
                .Where(al => al.Link == token && !al.IsDeleted)
                .Select(al => new ActionLinkTarget(
                    EF.Property<Guid>(al, "RequestId"),
                    al.Action,
                    al.ValidTo)),
            "FindActionLinkTarget",
            ct);

    private sealed record PublicRequestRow(
        string RequestorUserName,
        string RequestorEmail,
        ProjectShortName ProjectShortName,
        ProjectResourceId? ProjectResourceId,
        ProviderId? ProviderId,
        int RequestState,
        int RequestType,
        string? Slug);
}