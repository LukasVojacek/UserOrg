using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class ProjectTagRepository(DatabaseContext context) : GenericRepository<ProjectTag, Guid>(context), IProjectTagRepository
{
    public async Task<IList<ProjectTag>> GetAllByTagNameAsync(string? tagName, CancellationToken ct)
    {
        IQueryable<ProjectTag> tags = tagName switch
        {
            null => DbSet.Where(w => !w.IsDeleted),
            string => DbSet.Where(w => !w.IsDeleted && w.Name.Contains(tagName))
        };

        return await tags.ToListAsync(ct);
    }
}