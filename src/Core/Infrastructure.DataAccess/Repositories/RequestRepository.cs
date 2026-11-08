using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class RequestRepository(DatabaseContext context) : GenericRepository<Request, Guid>(context), IRequestRepository
{
    public async Task<Request> GetByIdWithUserAndProjectUser(Guid requestId, CancellationToken ct) => await DbSet.Include(x => x.Project).ThenInclude(x => x.CreatedByUser)
            .Include(x => x.User)
            .Include(x => x.ProjectResource).ThenInclude(x => x.AssignedProjects).ThenInclude(x => x.Project)
        .ThenInclude(x => x.AssignedProjectResources).ThenInclude(x => x.ProjectResource)
            .Include(x => x.Provider)
            .Where(x => x.Id == requestId)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
        ;

    public async Task<IList<PublicRequestPrepareModel>> GetByIdForPublicEndpoint(Guid requestId, CancellationToken ct) => await Context.Set<ActionLink>()
        .Where(x => x.RequestId == requestId && !x.IsDeleted)
        .Select(x => new PublicRequestPrepareModel
        {
            RequestId = x.RequestId,
            RequestorUserName = x.Request.User.UserName,
            RequestorEmail = x.Request.User.Email,
            ProjectShortName = x.Request.Project.ShortName,
            ProjectResourceName = x.Request.ProjectResourceName,
            ProviderName = x.Request.Provider.Name,
            RequestState = (int)x.Request.State,
            ValidTo = x.ValidTo,
            RequestType = (int)x.Request.Type,
            Action = x.Action,
            ActionLink = x.Link,
            IsDeleted = x.IsDeleted

        })
        .ToListAsync(ct)
        ;

    public async Task<List<Request>> GetByStateAndPm(ERequestState? state, ProjectShortName? projectShortName, string? projectManager, ERequestType? type, CancellationToken ct)
    {
        var query = DbSet.Include(x => x.Project).ThenInclude(x => x.CreatedByUser)
                        .Include(x => x.User)
                        .Include(x => x.Project).ThenInclude(x => x.UserPermissions)
                        .AsSplitQuery()
                        .AsQueryable();

        if (state.HasValue)
        {
            query = query.Where(x => x.State == state);
        }

        if (!string.IsNullOrEmpty(projectManager))
        {
            query = query.Where(x => x.Project.UserPermissions
                .Where(we => we.User.IdentityProviderUserId == projectManager && (we.SystemRoleId == 3 || we.SystemRoleId == 4))
                .Select(s => s.User.IdentityProviderUserId).Contains(projectManager));
        }

        if (projectShortName.HasValue)
        {
            query = query.Where(x => x.Project.ShortName == projectShortName);
        }

        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type);
        }

        return await query.ToListAsync(ct);

    }

    public async Task<List<Request>> GetForUser(ERequestState? state, ProjectShortName? projectShortName, string idpUserId, ERequestType? type, CancellationToken ct)
    {
        var query = DbSet.Include(x => x.Project).ThenInclude(x => x.CreatedByUser)
                        .Include(x => x.User)
                        .Include(x => x.Project)
                        .AsSplitQuery()
                        .Where(x => x.User.IdentityProviderUserId == idpUserId)
                        .AsQueryable();

        if (state.HasValue)
        {
            query = query.Where(x => x.State == state);
        }

        if (projectShortName.HasValue)
        {
            query = query.Where(x => x.Project.ShortName == projectShortName);
        }

        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type);
        }

        return await query.ToListAsync(ct);

    }

    public async Task<IList<Request>> GetRequestsForUser(string idpUserId, CancellationToken ct) => await DbSet.Where(x => x.User.IdentityProviderUserId == idpUserId && !x.IsDeleted)
            .Include(x => x.Project)
            .Include(x => x.ProjectResource)
            .ToListAsync(ct)
            ;

    public async Task<IList<Request>> GetRequestsForResource(string resourceName, CancellationToken ct) => await DbSet.Where(x => x.ProjectResourceName == resourceName && !x.IsDeleted && x.State == ERequestState.PendingForHPCProject)
            .Include(x => x.Project)
            .Include(x => x.ProjectResource)
            .Include(x => x.Provider)
            .Include(x => x.User)
            .AsSplitQuery()
            .ToListAsync(ct)
            ;
}