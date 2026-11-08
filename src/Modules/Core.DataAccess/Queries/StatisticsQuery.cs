using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

/// <summary>
/// Reads creation timestamps for the platform-wide statistics endpoint. <c>CreatedAt</c> is a shadow
/// property whose CLR type differs per entity (User/Project: <see cref="DateTimeOffset"/>,
/// ProjectResource: <see cref="DateTime"/>), so it is read via <c>EF.Property</c> and normalised to
/// UTC. The DbContext-wide <c>!IsDeleted</c> filter excludes soft-deleted rows.
/// </summary>
internal sealed class StatisticsQuery(
    IDataQuery<Domain.User.User> users,
    IDataQuery<Domain.Project.Project> projects,
    IDataQuery<ProjectResource> resources) : IStatisticsQuery
{
    public async Task<CreationTimestamps> GetCreationTimestampsAsync(CancellationToken ct)
    {
        var userDates = await users.ToListAsync(
            q => q.Select(u => EF.Property<DateTimeOffset>(u, "CreatedAt")),
            "Statistics.Users",
            ct);

        var projectDates = await projects.ToListAsync(
            q => q.Select(p => EF.Property<DateTimeOffset>(p, "CreatedAt")),
            "Statistics.Projects",
            ct);

        var resourceDates = await resources.ToListAsync(
            q => q.Select(r => EF.Property<DateTime>(r, "CreatedAt")),
            "Statistics.Resources",
            ct);

        return new CreationTimestamps(
            userDates.ConvertAll(d => d.UtcDateTime),
            projectDates.ConvertAll(d => d.UtcDateTime),
            resourceDates.ConvertAll(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)));
    }
}