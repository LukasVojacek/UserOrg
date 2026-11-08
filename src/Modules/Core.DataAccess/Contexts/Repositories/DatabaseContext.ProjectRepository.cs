using Core.DataAccess.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

internal sealed partial class DatabaseContext : IRepository<Domain.Project.Project, ProjectShortName>
{
    private IQueryable<Domain.Project.Project> _projectincludes => Projects
        // Bypass only the UserPermission "SystemRoleOnly" filter so _memberRoles (ProjectId set) hydrate, and
        // the link "AssignedResourceActive" and "AssignedUserActive" filters so soft-deleted links load (the
        // aggregate hides them and reactivates one on re-assign); the Project soft-delete filter and all others
        // stay active.
        .IgnoreQueryFilters(
        [
            "SystemRoleOnly",
            AssignedProjectResourceToProjectConfiguration.ActiveFilter,
            AssignedUserToProjectConfiguration.ActiveFilter,
        ])
        .AsSplitQuery()
        .Include("_projectDomainType")
        .Include("_projectTags")
        .Include("_assignedResources.ProjectResource")
        .Include("_assignedUsers.User")
        .Include("_memberRoles.User")
        .AsSplitQuery();
    async ValueTask<Domain.Project.Project?> IRepository<Domain.Project.Project, ProjectShortName>.AddAsync(Domain.Project.Project aggregate, CancellationToken cancellationToken)
    {
        await Projects.AddAsync(aggregate, cancellationToken);
        Entry(aggregate).Property("Id").CurrentValue = ProjectId.New();
        RegisterAddedAggregateRoot(aggregate);
        return await Task.FromResult(aggregate);
    }

    void IRepository<Domain.Project.Project, ProjectShortName>.Delete(Domain.Project.Project aggregate, CancellationToken cancellationToken) => Projects.Remove(aggregate);

    async ValueTask<Domain.Project.Project?> IRepository<Domain.Project.Project, ProjectShortName>.FindAsync(ProjectShortName key, CancellationToken cancellationToken) => await _projectincludes
        .FirstOrDefaultAsync(p => p.ShortName == key, cancellationToken);

}