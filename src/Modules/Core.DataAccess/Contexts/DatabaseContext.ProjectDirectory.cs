using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

/// <summary>
/// <see cref="IProjectDirectory"/> implementation on DatabaseContext. Projects the natural
/// <see cref="ProjectShortName"/> from a surrogate <see cref="ProjectId"/> for the write path
/// (scope-check + aggregate load of a project referenced by id), without hydrating the aggregate.
/// </summary>
internal sealed partial class DatabaseContext : IProjectDirectory
{
    async Task<ProjectShortName?> IProjectDirectory.FindShortNameAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        await Projects
            .Where(p => EF.Property<ProjectId>(p, "Id") == projectId)
            .Select(p => (ProjectShortName?)p.ShortName)
            .FirstOrDefaultAsync(cancellationToken);

    async Task<ProjectId?> IProjectDirectory.FindIdAsync(ProjectShortName shortName, CancellationToken cancellationToken) =>
        await Projects
            .Where(p => p.ShortName == shortName)
            .Select(p => (ProjectId?)EF.Property<ProjectId>(p, "Id"))
            .FirstOrDefaultAsync(cancellationToken);
}