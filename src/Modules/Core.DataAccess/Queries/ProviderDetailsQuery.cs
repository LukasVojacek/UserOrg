using Core.DataAccess.Configurations;
using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

internal sealed class ProviderDetailsQuery(
    IDataQuery<Domain.Provider.Provider> providerQuery,
    IDataQuery<AssignedLocation> assignedLocationQuery) : IProviderQuery
{
    public async Task<ProviderDetail?> GetProviderDetailsAsync(ProviderName name, IReadOnlyList<ProjectShortName>? scope, CancellationToken ct)
    {
        // Project the provider and a correlated subquery over its locations (joined by the shadow
        // ProviderId FK, since Provider has no CLR navigation to Location). The default ProviderActive and
        // location soft-delete filters apply, so only an active provider and its non-deleted locations come
        // back — without the allocation/assigned-value rows the write aggregate would load.
        var detail = await providerQuery.FirstOrDefaultAsync<Domain.Provider.Location, Country, ProviderDetail>(
            (providers, locations, countries) => SelectProviderDetails(providers.Where(p => p.Name == name), locations, countries),
            "GetProviderDetails",
            ct);

        if (detail is null)
            return null;

        // User read-scope tier (ADR-0018): a private provider resolves only when the caller reaches it through
        // one of their projects. Returning null here makes "hidden out of scope" indistinguishable from "no such
        // provider" — the same 404 (ADR-0029 keeps the contract on the natural key; the reachability check stays
        // inside the read on shadow ids). Public providers and the admin / provider tiers (scope == null) skip it.
        if (scope is not null && detail.IsPrivate && !await IsProviderReachableAsync(name, scope, ct))
            return null;

        return detail;
    }

    public async Task<QueryPage<ProviderDetail>> GetAdministeredProvidersAsync(AdministeredProviderFilter filter, int page, int limit, CancellationToken ct)
    {
        if (filter.Names is { Count: 0 })
            return new QueryPage<ProviderDetail>([], 0);

        var names = filter.Names?.ToList();
        return await providerQuery.ToPagedListAsync<Domain.Provider.Location, Country, ProviderDetail>(
            (providers, locations, countries) => SelectProviderDetails(
                (filter.IncludeInactive ? providers.IgnoreQueryFilters(["ProviderActive"]) : providers)
                    .WhereIf(names is not null, p => names!.Contains(p.Name))
                    .OrderBy(p => p.Name)
                    .ThenByTwinResolution(),
                locations,
                countries),
            page,
            limit,
            "GetAdministeredProviders",
            ct);
    }

    private static IQueryable<ProviderDetail> SelectProviderDetails(
        IQueryable<Domain.Provider.Provider> providers,
        IQueryable<Domain.Provider.Location> locations,
        IQueryable<Country> countries) =>
        providers.Select(p => new ProviderDetail(
            p.Name,
            p.Description,
            p.SupportEmail,
            p.ExternalId,
            p.Mode,
            p.ServiceName,
            p.IsPrivate,
            countries.Where(c => c.Id == p.CountryId).Select(c => c.Name).First().Value,
            p.IsDeleted,
            locations
                .Where(loc => EF.Property<ProviderId>(loc, "ProviderId") == EF.Property<ProviderId>(p, "Id"))
                .OrderBy(loc => loc.Name)
                .Select(loc => new ProviderLocationDetail(
                    loc.Name,
                    loc.Endpoint,
                    loc.Description,
                    loc.IsPrivate,
                    loc.IsShared))
                .ToList()));

    private async Task<bool> IsProviderReachableAsync(ProviderName name, IReadOnlyList<ProjectShortName> scope, CancellationToken ct)
    {
        if (scope.Count == 0)
            return false;

        var targetId = await providerQuery.FirstOrDefaultAsync<ProviderId?>(
            providers => providers.Where(p => p.Name == name).Select(p => (ProviderId?)EF.Property<ProviderId>(p, "Id")),
            "GetProviderIdByName",
            ct);

        if (targetId is not { } id)
            return false;

        var reachable = await ResolveReachableProviderIdsAsync(scope, ct);
        return reachable.Contains(id);
    }

    public Task<ProviderId?> FindActiveProviderIdByNameAsync(ProviderName name, CancellationToken ct) =>
        // Default ProviderActive query filter applies, so only an active provider resolves; the surrogate id stays
        // inside storage (returned as a value object, never on the Provider surface). Same named query as the
        // read-scope reachability check above so the two never drift.
        providerQuery.FirstOrDefaultAsync<ProviderId?>(
            providers => providers.Where(p => p.Name == name).Select(p => (ProviderId?)EF.Property<ProviderId>(p, "Id")),
            "GetProviderIdByName",
            ct);

    public async Task<bool> ProviderExistsAsync(ProviderName name, CancellationToken ct)
    {
        // IgnoreQueryFilters so Create also refuses a name held by a deactivated provider. IX_Provider_Name is
        // unique among active rows only; legacy data can hold name twins. Name comparison is case-insensitive
        // at the DB (SQL collation), so it matches regardless of the stored casing.
        var match = await providerQuery.FirstOrDefaultAsync(
            query => query.IgnoreQueryFilters().Where(p => p.Name == name).Select(p => p.Name.Value),
            "ProviderExists",
            ct);

        return match is not null;
    }

    public async Task<QueryPage<ProviderSummary>> GetProvidersAsync(ProviderFilter filter, int page, int limit, CancellationToken ct)
    {
        // User read-scope tier: pre-resolve the providers reachable from the caller's projects via the inverse
        // chain (project → assigned resource → assigned location → location → provider), so a private provider
        // the caller already reaches through a project stays visible. The chain spans four entity sets on its
        // own, so it cannot fold into the provider read below — resolve the (shadow) provider ids first, then OR
        // them with the public set inside the paged read so TotalItems counts the scoped union (ADR-0018).
        IReadOnlyList<ProviderId> reachable = filter.ScopeShortNames is { Count: > 0 } scope
            ? await ResolveReachableProviderIdsAsync(scope, ct)
            : [];

        // Description and SupportEmail are both nullable VOs mapped directly to their nullable columns
        // (null when unset). See ADR-0010. The OnlyPublic scope filter stays inside this builder so the
        // Count behind TotalItems reflects the scoped set, not the global table (ADR-0018).
        return await providerQuery.ToPagedListAsync<Country, ProviderSummary>(
            (query, countries) =>
                // Order inside the queryable so Skip/Take page at the database. Sort by the value object directly
                // (ProviderName is IComparable for the in-memory provider's client-side ordering); EF translates
                // this to ORDER BY the mapped column. Ordering by `p.Name.Value` is NOT SQL-translatable and threw
                // at runtime. Break ties on the unique shadow Id so paging is stable.
                ApplyFilter(query, countries, filter, reachable)
                    .OrderBy(p => p.Name)
                    .ThenBy(p => EF.Property<ProviderId>(p, "Id"))
                    .Select(p => new ProviderSummary(
                        p.Name,
                        p.Description,
                        p.SupportEmail,
                        p.ExternalId,
                        p.Mode,
                        p.ServiceName,
                        p.IsPrivate,
                        countries.Where(c => c.Id == p.CountryId).Select(c => c.Name).First().Value)),
            page,
            limit,
            "GetProviders",
            ct);
    }

    private static IQueryable<Domain.Provider.Provider> ApplyFilter(
        IQueryable<Domain.Provider.Provider> query,
        IQueryable<Country> countries,
        ProviderFilter filter,
        IReadOnlyList<ProviderId> reachable)
    {
        var code = filter.CountryCode is { } countryCode ? new CountryName(countryCode) : default;
        var own = filter.OwnProviderName ?? default;

        return query
            .WhereIf(!string.IsNullOrWhiteSpace(filter.Name?.Value), p => p.Name == filter.Name)
            .WhereIf(filter.CountryCode is not null, p => countries.Any(c => c.Id == p.CountryId && c.Name == code))
            .WhereIf(filter.OnlyPublic && filter.OwnProviderName is not null,
                p => !p.IsPrivate || p.Name == own || reachable.Contains(EF.Property<ProviderId>(p, "Id")))
            .WhereIf(filter.OnlyPublic && filter.OwnProviderName is null,
                p => !p.IsPrivate || reachable.Contains(EF.Property<ProviderId>(p, "Id")));
    }

    // The inverse read-scope chain (LocationDetailsQuery resolves it as far as the location; this adds the final
    // hop to the location's shadow ProviderId). Joins, not navigations: Provider has no CLR navigation to
    // Location, and AssignedLocation/Project reference each other through shadow FKs. Mirrors the soft-delete
    // guards (!al.IsDeleted, !ap.IsDeleted) so a retired assignment or project link does not re-expose a provider.
    private async Task<IReadOnlyList<ProviderId>> ResolveReachableProviderIdsAsync(
        IReadOnlyList<ProjectShortName> scope, CancellationToken ct)
    {
        var ids = await assignedLocationQuery.ToListAsync<AssignedProjectResourceToProject, Domain.Project.Project, Domain.Provider.Location, ProviderId>(
            (assignedLocations, assignedProjects, projects, locations) =>
                // Query syntax: four sets joined over shadow FKs with the !al.IsDeleted / !ap.IsDeleted / scope
                // guard(s) interleaved between the joins. The compiler's transparent identifiers keep every earlier
                // range variable in scope; the same chain as .Join() needs a hand-written anonymous carrier per step.
                // Same expression tree either way (module-conventions.md § LINQ syntax in read queries).
                from al in assignedLocations
                where !al.IsDeleted
                join ap in assignedProjects on EF.Property<ProjectResourceId>(al, "ProjectResourceId") equals EF.Property<ProjectResourceId>(ap, "ProjectResourceId")
                where !ap.IsDeleted
                join p in projects on EF.Property<ProjectId>(ap, "ProjectId") equals EF.Property<ProjectId>(p, "Id")
                where scope.Contains(p.ShortName)
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                select EF.Property<ProviderId>(loc, "ProviderId"),
            "GetScopedProviderIds",
            ct);

        return ids.Distinct().ToList();
    }
}