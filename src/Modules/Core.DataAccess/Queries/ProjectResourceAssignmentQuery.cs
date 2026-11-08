using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

/// <summary>
/// Authority-scoped write-validation read behind the resource↔project assignment endpoints (see
/// <see cref="IProjectResourceAssignmentQuery"/>). The object graph spans several entity sets joined over
/// shadow FKs, so the question is answered by one focused method-syntax query rather than a navigation load.
/// </summary>
internal sealed class ProjectResourceAssignmentQuery(
    IDataQuery<AssignedProjectResourceToProject> resourceToProject) : IProjectResourceAssignmentQuery
{
    public async Task<bool> ProjectHasNonSharedResourceAsync(ProjectShortName projectShortName, CancellationToken ct)
    {
        // Cross-aggregate (G6): does the project already hold an ACTIVE assigned resource whose resource sits on
        // an ACTIVE non-shared location? EXISTS-gated over the project link set. Filters !ap.IsDeleted and
        // !al.IsDeleted; the Project and Location query filters drop soft-deleted rows. Ports the legacy
        // non-shared rule faithfully — it inspects EXISTING assignments only (the incoming resource is not
        // examined here; ADR-0016 / F5 G6). The chain joins over the shadow FKs ProjectId, ProjectResourceId
        // and LocationId via EXISTS subqueries (no inner .Join over mismatched key types).
        var hits = await resourceToProject.ToListAsync<Domain.Project.Project, AssignedLocation, Location, int>(
            (links, projects, assignedLocations, locations) =>
                // Query syntax: four sets joined over shadow FKs with the !ap.IsDeleted / project short-name /
                // !al.IsDeleted / !loc.IsShared guard(s) interleaved between the joins. The compiler's transparent
                // identifiers keep every earlier range variable in scope; the same chain as .Join() needs a
                // hand-written anonymous carrier per step. Same expression tree either way (module-conventions.md §
                // LINQ syntax in read queries).
                from ap in links
                where !ap.IsDeleted
                join p in projects on EF.Property<ProjectId>(ap, "ProjectId") equals EF.Property<ProjectId>(p, "Id")
                where p.ShortName == projectShortName
                join al in assignedLocations on EF.Property<ProjectResourceId>(ap, "ProjectResourceId") equals EF.Property<ProjectResourceId>(al, "ProjectResourceId")
                where !al.IsDeleted
                join loc in locations on EF.Property<Guid>(al, "LocationId") equals EF.Property<Guid>(loc, "Id")
                where !loc.IsShared
                select 1,
            "ProjectHasNonSharedResource",
            ct);

        return hits.Count > 0;
    }
}