using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

/// <summary>
/// <see cref="IProjectDomainTypeLookup"/> implementation on DatabaseContext.
/// Returns a tracked <see cref="ProjectDomainType"/> so it can be attached to a project aggregate
/// loaded by the same context (see <c>DatabaseContext.ProjectRepository</c>).
/// </summary>
internal sealed partial class DatabaseContext : IProjectDomainTypeLookup
{
    async Task<ProjectDomainType?> IProjectDomainTypeLookup.FindByNameAsync(DomainTypeName name, CancellationToken cancellationToken) =>
        await Set<ProjectDomainType>().FirstOrDefaultAsync(d => d.Name == name, cancellationToken);
}