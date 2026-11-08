using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class ActionLinkRepository(DatabaseContext context) : GenericRepository<ActionLink, Guid>(context), IActionLinkRepository
{
    public async Task<ActionLink?> GetByLink(string actionLink, CancellationToken ct) => await DbSet
            .Include(x => x.Request).ThenInclude(r => r.Project)
            .FirstOrDefaultAsync(x => x.Link == actionLink && !x.IsDeleted && x.ValidTo > DateTime.UtcNow, ct)
            ;

    public async Task<IList<ActionLink>?> GetRelatedToRequest(Guid requestId, CancellationToken ct) => await DbSet
            .Include(x => x.Request).ThenInclude(r => r.Project)
            .AsSplitQuery()
            .Where(x => x.RequestId == requestId && !x.IsDeleted && x.ValidTo > DateTime.UtcNow)
            .ToListAsync(ct)
            ;
}