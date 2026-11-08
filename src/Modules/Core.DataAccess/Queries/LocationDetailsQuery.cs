using Core.DataAccess.Queries.Contracts;

using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

internal sealed class LocationDetailsQuery(
    IDataQuery<Location> locationQuery,
    IDataQuery<AssignedLocation> assignedLocationQuery) : ILocationQuery
{
    public async Task<QueryPage<LocationSummary>> GetLocationsAsync(LocationFilter filter, int page, int limit, CancellationToken ct)
    {
        var bounds = await ResolveReadBoundsAsync(filter, ct);
        if (bounds is null)
            return new QueryPage<LocationSummary>([], 0);

        var rootPage = await LoadLocationPageAsync(filter, bounds, page, limit, ct);
        if (rootPage.Items.Count == 0)
            return new QueryPage<LocationSummary>([], rootPage.TotalItems);

        var providerNamesByLocationId = await LoadProviderNamesAsync(rootPage.Items, ct);

        return new QueryPage<LocationSummary>(
            [.. rootPage.Items.Select(r => ToSummary(r, providerNamesByLocationId))],
            rootPage.TotalItems);
    }

    private sealed record ReadBounds(ProviderId? ProviderId, ProviderId? OwnProviderId, IReadOnlyList<Guid>? ScopeLocationIds);

    private async Task<ReadBounds?> ResolveReadBoundsAsync(LocationFilter filter, CancellationToken ct)
    {
        // Location has no CLR navigation to LocationType/Provider — they are mapped as unnamed FKs
        // (LocationTypeId, ProviderId shadow columns), so the provider/type lookups happen by id below.
        //
        // Provider tier: resolve the named provider to its (shadow) id so the root Location read can page
        // on the ProviderId shadow FK alone. A name that matches no provider yields an empty page, exactly
        // as the previous inner join did.
        ProviderId? providerId = null;
        if (filter.ProviderName is { } providerName)
        {
            providerId = await ResolveProviderIdAsync(providerName, ct);

            if (providerId is null)
                return null;
        }

        // Provider read-scope tier: the caller's own provider's locations are visible whether or not a project
        // reaches them, so they are OR-ed with the scoped ids below rather than narrowing the read.
        var ownProviderId = filter.OwnProviderName is { } ownProviderName
            ? await ResolveProviderIdAsync(ownProviderName, ct)
            : null;

        var scopeLocationIds = filter.ScopeShortNames is { } scope
            ? await ResolveScopeLocationIdsAsync(scope, ct)
            : null;

        return scopeLocationIds is { Count: 0 } && ownProviderId is null
            ? null
            : new ReadBounds(providerId, ownProviderId, scopeLocationIds);
    }

    private async Task<IReadOnlyList<Guid>> ResolveScopeLocationIdsAsync(IReadOnlyList<ProjectShortName> scope, CancellationToken ct)
    {
        // User read-scope tier: pre-resolve the locations reachable from the caller's projects
        // (project → assigned resource → assigned location), then constrain the main read to those ids.
        // The chain spans three entity sets on its own, so it cannot fold into the location read below.
        var ids = scope.Count == 0
            ? []
            : await assignedLocationQuery.ToListAsync<AssignedProjectResourceToProject, Domain.Project.Project, Guid>(
                (assignedLocations, assignedProjects, projects) =>
                    // Query syntax: three sets joined over shadow FKs with the !al.IsDeleted / !ap.IsDeleted /
                    // scope guard(s) interleaved between the joins. The compiler's transparent identifiers keep
                    // every earlier range variable in scope; the same chain as .Join() needs a hand-written
                    // anonymous carrier per step. Same expression tree either way (module-conventions.md § LINQ
                    // syntax in read queries).
                    from al in assignedLocations
                    where !al.IsDeleted
                    join ap in assignedProjects on EF.Property<ProjectResourceId>(al, "ProjectResourceId") equals EF.Property<ProjectResourceId>(ap, "ProjectResourceId")
                    where !ap.IsDeleted
                    join p in projects on EF.Property<ProjectId>(ap, "ProjectId") equals EF.Property<ProjectId>(p, "Id")
                    where scope.Contains(p.ShortName)
                    select EF.Property<Guid>(al, "LocationId"),
                "GetScopedLocationIds",
                ct);

        return ids.Distinct().ToList();
    }

    private Task<QueryPage<LocationRow>> LoadLocationPageAsync(LocationFilter filter, ReadBounds bounds, int page, int limit, CancellationToken ct)
    {
        var providerId = bounds.ProviderId;
        var hasOwnProvider = bounds.OwnProviderId is not null;
        var ownProvider = bounds.OwnProviderId ?? default;
        var scopeLocationIds = bounds.ScopeLocationIds;

        // Root-first paging: page the Location aggregate-root entity in SQL (Count + Skip/Take) so only
        // `limit` rows materialise plus a scoped count. Location's own (shadow) columns drive the filters;
        // the Provider set is referenced only by an EXISTS to drop deactivated-provider locations (see below).
        // Order by name, then the unique location Id as a tie-breaker. Soft-deleted locations stay excluded by
        // the global query filter.
        return locationQuery.ToPagedListAsync<Domain.Provider.Provider, LocationRow>(
            (locations, providers) =>
                locations
                    .WhereIf(providerId is not null, loc => EF.Property<ProviderId>(loc, "ProviderId") == providerId)
                    .WhereIf(filter.LocationType is not null, loc => loc.LocationType == filter.LocationType)
                    .WhereIf(scopeLocationIds is not null, loc =>
                        scopeLocationIds!.Contains(EF.Property<Guid>(loc, "Id"))
                        || (hasOwnProvider && EF.Property<ProviderId>(loc, "ProviderId") == ownProvider))
                    // Exclude locations whose provider is deactivated. The Provider set carries the
                    // "ProviderActive" global filter, so this EXISTS matches only live providers — keeping the
                    // count/page consistent with the provider-name stitch below (no KeyNotFoundException) and
                    // matching the pre-refactor inner-join result set (ADR-0018 status-observability rule:
                    // LocationSummary cannot express provider status, so such rows are excluded, not surfaced).
                    .Where(loc => providers.Any(p => EF.Property<ProviderId>(p, "Id") == EF.Property<ProviderId>(loc, "ProviderId")))
                    .OrderBy(loc => loc.Name)
                    .ThenBy(loc => EF.Property<Guid>(loc, "Id"))
                    .Select(loc => new LocationRow(
                        EF.Property<Guid>(loc, "Id"),
                        loc.Name.Value,
                        loc.Description,
                        loc.Endpoint,
                        loc.IsPrivate,
                        loc.IsShared,
                        loc.LocationType,
                        loc.Mode,
                        EF.Property<string>(loc, "DefaultServiceIntegrationUrl"))),
            page,
            limit,
            "GetLocations",
            ct);
    }

    private async Task<Dictionary<Guid, string>> LoadProviderNamesAsync(IReadOnlyList<LocationRow> rows, CancellationToken ct)
    {
        // Stage the provider lookup only for the page's rows and stitch, preserving the SQL page order. Location
        // has no CLR navigation to Provider (unnamed FK), so read the names by id.
        var providerIds = rows.Select(r => r.Id).ToList();
        var providerNames = await locationQuery.ToListAsync<Domain.Provider.Provider, ProviderRow>(
            (locations, providers) =>
                // Query syntax: two sets joined over shadow FKs with the page-id guard(s) interleaved between the
                // joins. The compiler's transparent identifiers keep every earlier range variable in scope; the same
                // chain as .Join() needs a hand-written anonymous carrier per step. Same expression tree either way
                // (module-conventions.md § LINQ syntax in read queries).
                from loc in locations
                where providerIds.Contains(EF.Property<Guid>(loc, "Id"))
                join prov in providers on EF.Property<ProviderId>(loc, "ProviderId") equals EF.Property<ProviderId>(prov, "Id")
                select new ProviderRow(EF.Property<Guid>(loc, "Id"), prov.Name.Value),
            "GetProviderNamesForPage",
            ct);

        return providerNames.ToDictionary(p => p.LocationId, p => p.ProviderName);
    }

    private static LocationSummary ToSummary(LocationRow r, Dictionary<Guid, string> providerNamesByLocationId) =>
        new(
            r.Name,
            r.Description,
            r.Endpoint,
            r.IsPrivate,
            r.IsShared,
            r.LocationType,
            providerNamesByLocationId[r.Id],
            r.Mode,
            r.DefaultServiceIntegrationUrl);

    public async Task<IList<LocationBaseData>> GetLocationsAsync(CancellationToken ct)
    {
        var locations = await locationQuery.ToListAsync<Location, LocationBaseData>(
            (_, locs) =>
                locs
                    .Select(loc => new LocationBaseData(
                        loc.Name.ToString(),
                        loc.Endpoint)),
            "GetLocations",
            ct);

        return locations;
    }

    private sealed record LocationRow(
        Guid Id,
        string Name,
        string? Description,
        string Endpoint,
        bool IsPrivate,
        bool IsShared,
        ELocationType LocationType,
        EOperationMode Mode,
        string? DefaultServiceIntegrationUrl);

    private sealed record ProviderRow(Guid LocationId, string ProviderName);

    public async Task<LocationSummary?> GetLocationByProviderAndNameAsync(
        ProviderName providerName, LocationName locationName, IReadOnlyList<ProjectShortName>? scope, CancellationToken ct)
    {
        // Resolve the (shadow) id by the natural key first — it never leaves this method (ADR-0029).
        var id = await ResolveLocationIdAsync(providerName, locationName, ct);
        if (id is not { } locationId)
            return null;

        // User read-scope tier (ADR-0018): a location resolves only when the caller reaches it through one of
        // their projects (project → assigned resource → assigned location). Returning null makes "out of scope"
        // and "no such location" the same 404. Admin / provider identity (scope == null) skip the check.
        if (scope is not null && !await IsLocationReachableAsync(locationId, scope, ct))
            return null;

        return await LoadLocationAsync(locationId, ct);
    }

    private Task<ProviderId?> ResolveProviderIdAsync(ProviderName providerName, CancellationToken ct) =>
        locationQuery.FirstOrDefaultAsync<Domain.Provider.Provider, ProviderId?>(
            (_, providers) =>
                providers
                    .Where(prov => prov.Name == providerName)
                    .Select(prov => (ProviderId?)EF.Property<ProviderId>(prov, "Id")),
            "GetProviderIdByName",
            ct);

    private Task<Guid?> ResolveLocationIdAsync(ProviderName providerName, LocationName locationName, CancellationToken ct) =>
        locationQuery.FirstOrDefaultAsync<Domain.Provider.Provider, Guid?>(
            (locations, providers) =>
                // Query syntax: two sets joined over shadow FKs with the provider-name + location-name guard(s)
                // interleaved between the joins. The compiler's transparent identifiers keep every earlier range
                // variable in scope; the same chain as .Join() needs a hand-written anonymous carrier per step. Same
                // expression tree either way (module-conventions.md § LINQ syntax in read queries).
                from loc in locations
                join prov in providers on EF.Property<ProviderId>(loc, "ProviderId") equals EF.Property<ProviderId>(prov, "Id")
                where prov.Name == providerName && loc.Name == locationName
                select (Guid?)EF.Property<Guid>(loc, "Id"),
            "GetLocationIdByProviderAndName",
            ct);

    public async Task<bool> LocationTypeExistsAsync(ELocationType locationType, CancellationToken ct)
    {
        var found = await locationQuery.FirstOrDefaultAsync<LocationType, ELocationType?>(
            (_, types) => types
                .Where(t => t.Id == locationType)
                .Select(t => (ELocationType?)t.Id),
            "LocationTypeExists",
            ct);

        return found.HasValue;
    }

    private async Task<bool> IsLocationReachableAsync(Guid locationId, IReadOnlyList<ProjectShortName> scope, CancellationToken ct)
    {
        if (scope.Count == 0)
            return false;

        // Targeted reachability for one location: does the caller's project scope reach this exact location id
        // through an active assignment + project link? Mirrors the scoped-list chain's soft-delete guards.
        var hit = await assignedLocationQuery.FirstOrDefaultAsync<AssignedProjectResourceToProject, Domain.Project.Project, int>(
            (assignedLocations, assignedProjects, projects) =>
                // Query syntax: three sets joined over shadow FKs with the !al.IsDeleted / !ap.IsDeleted / scope
                // guard(s) interleaved between the joins. The compiler's transparent identifiers keep every earlier
                // range variable in scope; the same chain as .Join() needs a hand-written anonymous carrier per step.
                // Same expression tree either way (module-conventions.md § LINQ syntax in read queries).
                from al in assignedLocations
                where !al.IsDeleted && EF.Property<Guid>(al, "LocationId") == locationId
                join ap in assignedProjects on EF.Property<ProjectResourceId>(al, "ProjectResourceId") equals EF.Property<ProjectResourceId>(ap, "ProjectResourceId")
                where !ap.IsDeleted
                join p in projects on EF.Property<ProjectId>(ap, "ProjectId") equals EF.Property<ProjectId>(p, "Id")
                where scope.Contains(p.ShortName)
                select 1,
            "IsLocationReachable",
            ct);

        return hit == 1;
    }

    private Task<LocationSummary?> LoadLocationAsync(Guid id, CancellationToken ct) =>
        locationQuery.FirstOrDefaultAsync<Domain.Provider.Provider, LocationSummary>(
            (locations, providers) =>
                // Query syntax: two sets joined over shadow FKs with the by-id guard interleaved between the
                // joins. The compiler's transparent identifiers keep every earlier range variable in scope; the same
                // chain as .Join() needs a hand-written anonymous carrier per step. Same expression tree either way
                // (module-conventions.md § LINQ syntax in read queries).
                from loc in locations
                join prov in providers on EF.Property<ProviderId>(loc, "ProviderId") equals EF.Property<ProviderId>(prov, "Id")
                where EF.Property<Guid>(loc, "Id") == id
                select new LocationSummary(
                    loc.Name.Value,
                    loc.Description,
                    loc.Endpoint,
                    loc.IsPrivate,
                    loc.IsShared,
                    loc.LocationType,
                    prov.Name.Value,
                    loc.Mode,
                    EF.Property<string>(loc, "DefaultServiceIntegrationUrl")),
            "GetLocation",
            ct);
}