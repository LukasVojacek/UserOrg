using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

internal sealed class ProjectTagDetailsQuery(
    IDataQuery<ProjectTag> projectTagQuery,
    IDataQuery<Domain.Project.Project> projectQuery) : IProjectTagQuery
{
    public Task<QueryPage<ProjectTagSummary>> GetProjectTagsAsync(ProjectTagFilter filter, int page, int limit, CancellationToken ct)
    {
        return projectTagQuery.ToPagedListAsync(
            query =>
            {
                var q = query.Where(pt => !pt.IsDeleted);

                if (filter.TagName is { Length: > 0 } tn)
                {
                    var tagName = new TagName(tn);
                    q = q.Where(pt => pt.Name == tagName);
                }

                // Order by name, then the unique Id as a tie-breaker so rows don't shuffle across pages.
                return q
                    .OrderBy(pt => pt.Name)
                    .ThenBy(pt => EF.Property<Guid>(pt, "Id"))
                    .Select(pt => new ProjectTagSummary(
                        pt.Name.Value));
            },
            page,
            limit,
            "GetProjectTags",
            ct);
    }

    public Task<ProjectTagSummary?> GetProjectTagAsync(Guid id, CancellationToken ct) =>
        projectTagQuery.FirstOrDefaultAsync(
            query => query
                .Where(pt => !pt.IsDeleted && EF.Property<Guid>(pt, "Id") == id)
                .Select(pt => new ProjectTagSummary(
                    pt.Name.Value)),
            "GetProjectTag",
            ct);

    public Task<ProjectTagSummary?> GetProjectTagByNameAsync(TagName name, CancellationToken ct) =>
        projectTagQuery.FirstOrDefaultAsync(
            query => query
                .Where(pt => !pt.IsDeleted && pt.Name == name)
                .Select(pt => new ProjectTagSummary(
                    pt.Name.Value)),
            "GetProjectTagByName",
            ct);

    public async Task<bool> IsTagAssignedToAnyProjectAsync(TagName tagName, CancellationToken ct)
    {
        // Project↔ProjectTag is a many-to-many with no join entity class; traverse the "_projectTags" skip
        // navigation from the project side (as ProjectDetailsQuery does) — SelectMany over the navigation
        // translates on both SQL Server and the in-memory provider, unlike a nested correlated .Any() over
        // the shadow FK. The Project soft-delete query filter excludes inactive projects; !t.IsDeleted drops
        // a soft-deleted catalogue entry.
        var hits = await projectQuery.ToListAsync(
            query => query
                .SelectMany(p => EF.Property<IEnumerable<ProjectTag>>(p, "_projectTags"))
                .Where(t => !t.IsDeleted && t.Name == tagName)
                .Select(_ => 1),
            "IsTagAssignedToAnyProject",
            ct);

        return hits.Count > 0;
    }
}