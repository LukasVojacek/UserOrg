using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries;

/// <summary>
/// Caller-scoped reads for resources (legacy <c>ProjectResource</c>). A resource is visible only if it
/// is assigned to a project in the caller's scope. The object graph (resource → assignment → location →
/// specification values) spans more entity sets than a single <see cref="IDataQuery{T}"/> join supports
/// and the relationships are shadow FKs, so it is loaded in stages and assembled in memory — mirroring
/// the extended-project resource read in <see cref="ProjectDetailsQuery"/>.
/// </summary>
internal sealed class ResourceDetailsQuery(
    IDataQuery<ProjectResource> resources,
    IDataQuery<AssignedLocation> assignedLocations,
    IDataQuery<AssignedLocationValue> assignedLocationValues,
    IDataQuery<AssignedProjectResourceToProject> assignedProjects) : IResourceQuery
{
    private sealed record ResourceScalarRow(
        Guid Id, string Name, ProviderName? ProviderName, string? Description, Email? Email,
        DateTimeOffset StartDate, DateTimeOffset EndDate, EOperationMode Mode, string? LicenseUrl, EProjectResourceState State, Email? CreatedByEmail)
    {
        // Null — never a half-formed "|name" — for a legacy standalone row, which carries no owning
        // provider and therefore has no per-provider natural key (ADR-0044 §1).
        internal string? ProviderResourceName => ProviderName.HasValue ? $"{ProviderName.Value}|{Name}" : default;
    };

    // Id/ProjectResourceId stay internal — Id keys the spec lookup, ProjectResourceId groups rows by
    // resource — but neither (nor LocationId) is exposed: the allocation is addressed by Slug (ADR-0024).
    private sealed record AssignmentRow(
        Guid Id, Guid ProjectResourceId, string? Slug, ELocationType LocationType, bool IsLocationShared,
        string LocationName, long? AllocationAmount, int AllocationUnitTypeId, string? AggregationName, int Mode);

    private sealed record SpecRow(Guid AssignedLocationId, string Name, string Value);

    public async Task<QueryPage<ResourceSummary>> GetResourcesAsync(ResourceFilter filter, int page, int limit, CancellationToken ct)
    {
        // Root-first paging: page the ProjectResource root in SQL (Count + Skip/Take), then stage the
        // child reads (assignments → location, specification values) for that page's resource ids only and
        // stitch — mirroring ProjectDetailsQuery.GetProjectUsersAsync. The graph still assembles in memory
        // because the relationships are shadow FKs spanning more sets than one IDataQuery join, but only the
        // page's rows are materialised. Order by name, then the unique resource Id as a tie-breaker so rows
        // don't shuffle across pages. TotalItems reflects the scoped set.
        var (candidateResourceIds, empty) = await ResolveScopeAsync(
            filter.ScopeShortNames, filter.ScopeProviderName, filter.ProjectShortName, filter.FilterProviderName, ct);
        if (empty)
            return new QueryPage<ResourceSummary>([], 0);

        // A location-type filter keeps a resource that has at least one active assignment of that type
        // (legacy semantics). Resolve those resource ids up front and intersect, so the SQL count and page
        // reflect the filtered set — the post-assembly filter previously did this after materialising all.
        if (filter.LocationType is { } lt)
        {
            candidateResourceIds = await NarrowToLocationTypeAsync(candidateResourceIds, lt, ct);
            if (candidateResourceIds.Count == 0)
                return new QueryPage<ResourceSummary>([], 0);
        }

        var scalarPage = await LoadScalarPageAsync(candidateResourceIds, filter.StartDate, filter.EndDate, page, limit, ct);
        if (scalarPage.Items.Count == 0)
            return new QueryPage<ResourceSummary>([], scalarPage.TotalItems);

        var pageIds = scalarPage.Items.Select(r => ProjectResourceId.From(r.Id)).ToList();
        var assignmentsByResource = await LoadAssignmentsByResourceAsync(pageIds, ct);

        return new QueryPage<ResourceSummary>(
            [.. scalarPage.Items.Select(r => ToSummary(r, assignmentsByResource))],
            scalarPage.TotalItems);
    }

    private async Task<IReadOnlyList<ProjectResourceId>> NarrowToLocationTypeAsync(
        IReadOnlyList<ProjectResourceId>? candidateResourceIds, ELocationType locationType, CancellationToken ct)
    {
        var idsWithType = await ResourceIdsWithLocationTypeAsync(locationType, ct);
        return candidateResourceIds is null
            ? idsWithType
            : [.. candidateResourceIds.Intersect(idsWithType)];
    }

    private static ResourceSummary ToSummary(ResourceScalarRow r, Dictionary<Guid, IReadOnlyList<ResourceAssignmentSummary>> assignmentsByResource) =>
        new(
            r.Name,
            r.ProviderResourceName,
            r.Description,
            r.Email?.Value is { Length: > 0 } email ? email : null,
            r.StartDate,
            r.EndDate,
            r.Mode,
            r.LicenseUrl,
            r.State,
            r.CreatedByEmail,
            assignmentsByResource.GetValueOrDefault(r.Id, []));

    private Task<QueryPage<ResourceScalarRow>> LoadScalarPageAsync(
        IReadOnlyList<ProjectResourceId>? candidateResourceIds,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        int page,
        int limit,
        CancellationToken ct) =>
        // Stage 0 — page the root resource rows in SQL. No project join, so project-less resources survive.
        // Order/filter on the converted properties directly (not their .Value): `pr.ResourceName.Value` in
        // ORDER BY and `EF.Property<…>(pr,"Id").Value` in a paged WHERE are not server-translatable and throw
        // "could not be translated" — see the v2/resources regression note in
        // docs/ddd-migration/legacy-to-refactored-endpoint-migration.md.
        resources.ToPagedListAsync<Domain.Provider.Provider, User, ResourceScalarRow>(
            (projectResources, providerSet, userSet) =>
                projectResources
                .Where(pr => (candidateResourceIds == null || candidateResourceIds.Contains(EF.Property<ProjectResourceId>(pr, "Id")))
                    && (startDate == null || pr.StartDate >= startDate)
                    && (endDate == null || pr.EndDate <= endDate))
                // The owning provider's name cannot be read as pr.ProviderName: that getter resolves the private
                // _provider navigation, which nothing populates here — EF gives up translating it, evaluates the
                // projection client-side over an entity whose navigation is null, and the name silently comes
                // back empty for every row. Read it instead as a correlated subquery over the shadow FK, which
                // SQL renders as a scalar sub-select. Preferred over a GroupJoin/DefaultIfEmpty outer join: it
                // cannot multiply rows by construction, which matters because the Count/Skip/Take below page
                // exactly this query, and it keeps the composer a plain Where/OrderBy/Select. A NULL ProviderId
                // (legacy standalone row) matches nothing and yields null — never a half-formed "|name".
                // Query filters are ignored on purpose — the name is the resource's identity, not a visibility
                // decision, and the rows were already scoped by the caller's tier, so a deactivated provider must
                // not silently strip the natural key off a resource the caller may see.
                .OrderBy(pr => pr.ResourceName)
                .ThenBy(pr => EF.Property<ProjectResourceId>(pr, "Id"))
                .Select(pr => new ResourceScalarRow(
                    EF.Property<ProjectResourceId>(pr, "Id").Value,
                    pr.ResourceName.Value,
                    providerSet.IgnoreQueryFilters()
                        .Where(prov => (ProviderId?)EF.Property<ProviderId>(prov, "Id") == EF.Property<ProviderId?>(pr, "ProviderId"))
                        .Select(prov => (ProviderName?)prov.Name)
                        .FirstOrDefault(),
                    pr.Description,
                    pr.PrimaryInvestigatorEmail,
                    pr.StartDate,
                    pr.EndDate,
                    pr.OperationMode,
                    pr.LicenseUrl,
                    pr.State,
                    userSet
                        .Where(user => pr.CreatedBy != null && EF.Property<UserId>(user, "Id") == pr.CreatedBy)
                        .Select(user => (Email)user.Email)
                        .FirstOrDefault()
                    )),
            page,
            limit,
            "GetResources",
            ct);

    // Resolve the caller-scoped candidate resource ids (null = admin, no restriction) plus the scope logic's
    // short-circuit (empty scope / empty intersection ⇒ no rows). Shared by the paged list path; the single
    // GetResourceAsync keeps using LoadAsync.
    private async Task<(IReadOnlyList<ProjectResourceId>? Ids, bool Empty)> ResolveScopeAsync(
        IReadOnlyList<ProjectShortName>? scope,
        ProviderName? providerScope,
        ProjectShortName? projectShortName,
        ProviderName? filterProviderName,
        CancellationToken ct)
    {
        // The read-scope tier (caller's projects, own provider, or admin = all) plus optional narrowing
        // filters (project short-name; provider path scope of the provider-nested routes). Each contributes a
        // set of resource ids; the candidate set is their intersection. No tier and no filters ⇒ admin / all.
        var (tier, ownProviderResourceIds) = await ResolveTierAsync(scope, providerScope, ct);
        if (tier is { Count: 0 })
            return (null, true);

        var sets = await ResolveFilterSetsAsync(projectShortName, filterProviderName, providerScope, ownProviderResourceIds, ct);
        if (tier is not null)
            sets.Insert(0, tier);

        if (sets.Count == 0)
            return (null, false);

        IReadOnlyList<ProjectResourceId> candidate = sets.Aggregate(static (acc, next) => [.. acc.Intersect(next)]);

        return (candidate, candidate.Count == 0);
    }

    private async Task<(List<ProjectResourceId>? Tier, List<ProjectResourceId>? OwnProviderResourceIds)> ResolveTierAsync(
        IReadOnlyList<ProjectShortName>? scope, ProviderName? providerScope, CancellationToken ct)
    {
        if (scope is null && providerScope is null)
            return (null, null);

        // The two halves of the tier are a UNION: a provider identity sees its own provider's resources
        // whether or not a project reaches them, without losing the ones its projects already reach.
        List<ProjectResourceId> projectResourceIds = scope is { Count: > 0 }
            ? await ResourceIdsForProjectsAsync(scope, ct)
            : [];
        var ownProviderResourceIds = providerScope is { } pn
            ? await ResourceIdsForProviderAsync(pn, ct)
            : null;

        return ([.. projectResourceIds.Concat(ownProviderResourceIds ?? []).Distinct()], ownProviderResourceIds);
    }

    private async Task<List<IReadOnlyList<ProjectResourceId>>> ResolveFilterSetsAsync(
        ProjectShortName? projectShortName,
        ProviderName? filterProviderName,
        ProviderName? providerScope,
        List<ProjectResourceId>? ownProviderResourceIds,
        CancellationToken ct)
    {
        List<IReadOnlyList<ProjectResourceId>> sets = [];

        if (projectShortName is { } psn)
            sets.Add(await ResourceIdsForProjectsAsync([psn], ct));

        if (filterProviderName is { } fpn)
        {
            // The route's provider filter is the same provider as the caller's own-provider tier when a
            // ProviderAdmin browses their own provider's route — reuse that tier's result instead of
            // re-querying the identical provider (still added to sets, so the intersection still narrows).
            sets.Add(providerScope == fpn && ownProviderResourceIds is { } reused
                ? reused
                : await ResourceIdsForProviderAsync(fpn, ct));
        }

        return sets;
    }

    // Resource ids that have at least one active assignment of the given location type — used to apply the
    // location-type filter before paging the root, preserving the legacy "keep the resource, list all its
    // assignments" semantics.
    private async Task<List<ProjectResourceId>> ResourceIdsWithLocationTypeAsync(ELocationType locationType, CancellationToken ct)
    {
        var ids = await assignedLocations.ToListAsync<Location, ProjectResourceId>(
            (assigned, locations) =>
                from al in assigned
                where !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                where loc.LocationType == locationType
                select EF.Property<ProjectResourceId>(al, "ProjectResourceId"),
            "GetResourceIdsWithLocationType",
            ct);

        return ids.Distinct().ToList();
    }

    // Stage B + C for a set of resource ids: active assignments (joined to location) and their spec values,
    // grouped by resource. Shared by the paged list (page ids) and the single-resource LoadAsync path.
    private async Task<Dictionary<Guid, IReadOnlyList<ResourceAssignmentSummary>>> LoadAssignmentsByResourceAsync(
        IReadOnlyList<ProjectResourceId> resourceIds,
        CancellationToken ct)
    {
        // Stage B — active assignments for those resources, joined to their location. AssignedLocation has
        // no global soft-delete query filter, so exclude deleted allocations explicitly (legacy parity:
        // ProjectResourceRepository includes AssignedLocations.Where(!IsDeleted)).
        var assignmentRows = await assignedLocations.ToListAsync<Location, AssignmentRow>(
            (assigned, locations) =>
                from al in assigned
                where resourceIds.Contains(EF.Property<ProjectResourceId>(al, "ProjectResourceId")) && !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                select new AssignmentRow(
                    EF.Property<Guid>(al, "Id"),
                    EF.Property<ProjectResourceId>(al, "ProjectResourceId").Value,
                    al.Slug,
                    loc.LocationType,
                    loc.IsShared,
                    loc.Name.Value,
                    al.AllocationUnitCount,
                    EF.Property<int>(al, "AllocationUnitTypeId"),
                    al.AggregationName,
                    (int)al.Mode),
            "GetResourceAssignments",
            ct);

        // Stage C — specification values for those assignments.
        var assignedLocationIds = assignmentRows.Select(a => a.Id).ToList();
        var specRows = assignedLocationIds.Count == 0
            ? []
            : await assignedLocationValues.ToListAsync(
                query => query
                    .Where(v => assignedLocationIds.Contains(EF.Property<Guid>(v, "AssignedLocationId")))
                    .Select(v => new SpecRow(EF.Property<Guid>(v, "AssignedLocationId"), v.Name, v.Value)),
                "GetResourceAssignmentValues",
                ct);

        var specsByAssignment = specRows
            .GroupToLists(s => s.AssignedLocationId, s => new SpecificationSummary(s.Name, s.Value));

        return assignmentRows
            .GroupToLists(
                a => a.ProjectResourceId,
                a => new ResourceAssignmentSummary(
                    a.Slug,
                    a.LocationType,
                    a.IsLocationShared,
                    a.LocationName,
                    a.AllocationAmount,
                    (EAllocationUnitType)a.AllocationUnitTypeId,
                    a.AggregationName,
                    (EOperationMode)a.Mode,
                    specsByAssignment.GetValueOrDefault(a.Id, [])));
    }

    public async Task<ResourceSummary?> GetResourceAsync(Guid id, IReadOnlyList<ProjectShortName> scope, CancellationToken ct)
    {
        var found = await LoadAsync(scope, null, null, null, null, id, null, null, ct);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<ResourceSummary?> GetResourceByNameAsync(ResourceName name, IReadOnlyList<ProjectShortName> scope, CancellationToken ct)
    {
        var found = await LoadAsync(scope, null, null, null, null, null, name, null, ct);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<ResourceSummary?> GetResourceByProviderAndNameAsync(ProviderName providerName, ResourceName name, IReadOnlyList<ProjectShortName>? scope, CancellationToken ct)
    {
        var found = await LoadAsync(scope, null, null, null, null, null, name, providerName, ct);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<bool> IsResourceNameTakenAsync(ProviderName providerName, ResourceName name, CancellationToken ct)
    {
        // Per-provider name uniqueness (ADR-0026/0031). True when an ACTIVE resource carries `name` under the
        // active provider `providerName` — an EXISTS gate over the provider set (mirrors the by-name resolver).
        // The ProjectResource !IsDeleted filter applies, so soft-deleted history never blocks a re-add (matches
        // the filtered unique index); the provider's surrogate id stays server-side (ADR-0029). The create
        // use-case has already loaded the Provider for the FK, so resolution is by natural key only.
        var taken = await resources.FirstOrDefaultAsync<Domain.Provider.Provider, int?>(
            (res, prov) => res
                .Where(pr => pr.ResourceName == name
                    && prov.Any(p =>
                        p.Name == providerName
                        && EF.Property<ProviderId>(p, "Id") == EF.Property<ProviderId?>(pr, "ProviderId")))
                .Select(pr => (int?)1),
            "IsResourceNameTaken",
            ct);

        return taken.HasValue;
    }

    private async Task<List<ResourceSummary>> LoadAsync(
        IReadOnlyList<ProjectShortName>? scope,
        ProviderName? providerScope,
        ProjectShortName? projectShortName,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        Guid? id,
        ResourceName? name,
        ProviderName? filterProviderName,
        CancellationToken ct)
    {
        // Read-scope tier (caller's projects / provider tier / admin = all) intersected with the optional
        // narrowing filters (projectShortName; the provider path scope of the provider-nested routes). Shared
        // with the paged list path. The scalar read below never joins projects, so admin/provider tiers still
        // see project-less resources.
        var (candidateResourceIds, empty) = await ResolveScopeAsync(scope, providerScope, projectShortName, filterProviderName, ct);
        if (empty)
        {
            return [];
        }

        var hasId = id.HasValue;
        var targetId = ProjectResourceId.From(id ?? Guid.Empty);
        var hasName = name.HasValue;
        var targetName = name ?? default;

        // Stage A — scalar rows for the candidate resources (or all, for admin), with optional
        // by-id / by-name / validity-date filters. No project join, so project-less resources survive.
        var resourceRows = await resources.ToListAsync<Domain.Provider.Provider, User, ResourceScalarRow>(
            (projectResources, providerSet, userSet) =>
                projectResources
                .WhereIf(candidateResourceIds is not null, pr => candidateResourceIds!.Contains(EF.Property<ProjectResourceId>(pr, "Id")))
                .WhereIf(hasId, pr => EF.Property<ProjectResourceId>(pr, "Id") == targetId)
                .WhereIf(hasName, pr => pr.ResourceName == targetName)
                .WhereIf(startDate is not null, pr => pr.StartDate >= startDate)
                .WhereIf(endDate is not null, pr => pr.EndDate <= endDate)
                // The owning provider's name cannot be read as pr.ProviderName: that getter resolves the private
                // _provider navigation, which nothing populates here — EF gives up translating it, evaluates the
                // projection client-side over an entity whose navigation is null, and the name silently comes
                // back empty for every row. Read it instead as a correlated subquery over the shadow FK, which
                // SQL renders as a scalar sub-select. Preferred over a GroupJoin/DefaultIfEmpty outer join: it
                // cannot multiply rows by construction, which matters because the Count/Skip/Take below page
                // exactly this query, and it keeps the composer a plain Where/OrderBy/Select. A NULL ProviderId
                // (legacy standalone row) matches nothing and yields null — never a half-formed "|name".
                // Query filters are ignored on purpose — the name is the resource's identity, not a visibility
                // decision, and the rows were already scoped by the caller's tier, so a deactivated provider must
                // not silently strip the natural key off a resource the caller may see.
                .Select(pr => new ResourceScalarRow(
                    EF.Property<ProjectResourceId>(pr, "Id").Value,
                    pr.ResourceName.Value,
                    providerSet.IgnoreQueryFilters()
                        .Where(prov => (ProviderId?)EF.Property<ProviderId>(prov, "Id") == EF.Property<ProviderId?>(pr, "ProviderId"))
                        .Select(prov => (ProviderName?)prov.Name)
                        .FirstOrDefault(),
                    pr.Description,
                    pr.PrimaryInvestigatorEmail,
                    pr.StartDate,
                    pr.EndDate,
                    pr.OperationMode,
                    pr.LicenseUrl,
                    pr.State,
                    userSet
                        .Where(user => pr.CreatedBy != null && EF.Property<UserId>(user, "Id") == pr.CreatedBy)
                        .Select(user => (Email)user.Email)
                        .FirstOrDefault())),
            "GetResources",
            ct);

        var distinctResources = resourceRows.DistinctBy(r => r.Id).ToList();
        if (distinctResources.Count == 0)
        {
            return [];
        }

        var candidateIds = distinctResources.Select(r => ProjectResourceId.From(r.Id)).ToList();

        // Stage B + C — assignments (joined to location) and spec values for those resources, grouped by
        // resource. Shared with the paged list path.
        var assignmentsByResource = await LoadAssignmentsByResourceAsync(candidateIds, ct);

        return [.. distinctResources.Select(r => ToSummary(r, assignmentsByResource))];
    }

    // Resource ids assigned to any of the given projects (active links only). Used for the user tier and
    // for the projectShortName search; a resource may repeat per project, so callers de-duplicate.
    private async Task<List<ProjectResourceId>> ResourceIdsForProjectsAsync(IReadOnlyList<ProjectShortName> shortNames, CancellationToken ct)
    {
        var ids = await assignedProjects.ToListAsync<Domain.Project.Project, ProjectResourceId>(
            (assignedProjectLinks, projects) =>
                from ap in assignedProjectLinks
                where !ap.IsDeleted
                join p in projects on EF.Property<ProjectId>(ap, "ProjectId") equals EF.Property<ProjectId>(p, "Id")
                where shortNames.Contains(p.ShortName)
                select EF.Property<ProjectResourceId>(ap, "ProjectResourceId"),
            "GetResourceIdsForProjects",
            ct);

        return ids.Distinct().ToList();
    }

    // Resource ids assigned to a location belonging to the provider (active assignments only) — the
    // provider tier. Independent of project attachment, mirroring legacy ProjectResourceRepository.
    private async Task<List<ProjectResourceId>> ResourceIdsForProviderAsync(ProviderName providerName, CancellationToken ct)
    {
        var ids = await assignedLocations.ToListAsync<Location, Domain.Provider.Provider, ProjectResourceId>(
            (assigned, locations, providers) =>
                from al in assigned
                where !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                join prov in providers on EF.Property<ProviderId>(loc, "ProviderId") equals EF.Property<ProviderId>(prov, "Id")
                where prov.Name == providerName
                select EF.Property<ProjectResourceId>(al, "ProjectResourceId"),
            "GetResourceIdsForProvider",
            ct);

        return ids.Distinct().ToList();
    }
}