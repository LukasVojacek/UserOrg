using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class UserRepository(DatabaseContext context) : GenericRepository<User, Guid>(context), IUserRepository
{
    public async Task<IEnumerable<User>> GetAllAsync(Domain.ValueObjects.ProjectShortName? projectShortName, string? idpUserId, CancellationToken ct)
    {
        var query = DbSet
            .Include(x => x.AssignedToProjects).AsQueryable();

        if (projectShortName.HasValue)
        {
            query = query
                .Include(x => x.AssignedToProjects).ThenInclude(x => x.Project)
                .Where(x => x.AssignedToProjects.Any(x => x.Project.ShortName == projectShortName))
                .AsQueryable();
        }

        if (!string.IsNullOrEmpty(idpUserId))
        {
            query = query
                .Where(x => x.IdentityProviderUserId == idpUserId)
                .AsQueryable();
        }

        return await query
            .Where(u => !u.IsDeleted)
            .Select(x => new User()
            {
                IdentityProviderUserId = x.IdentityProviderUserId,
                Email = x.Email,
                UserName = x.UserName,
                CreatedAt = x.CreatedAt,
                ModifiedAt = x.ModifiedAt,
                IsDeleted = x.IsDeleted,
                AssignedToProjects = x.AssignedToProjects.Where(u => !u.IsDeleted)
                    .Select(y => new AssignedUserToProject()
                    {
                        AssignedByUser = y.AssignedByUser,
                        Project = y.Project,
                        ProjectId = y.ProjectId
                    }).ToList(),
                Id = x.Id,
                AssignedProvider = x.AssignedProvider
            })
            .ToListAsync(ct);
    }
    public async Task<IEnumerable<User>> GetAllWithoutProjectAsync(CancellationToken ct)
    {
        var query = DbSet
            .Include(x => x.AssignedToProjects)
            .Where(x => !x.AssignedToProjects.Any())
            .AsQueryable();

        return await query
            .Where(u => !u.IsDeleted)
            .Select(x => new User()
            {
                IdentityProviderUserId = x.IdentityProviderUserId,
                Email = x.Email,
                UserName = x.UserName,
                CreatedAt = x.CreatedAt,
                ModifiedAt = x.ModifiedAt,
                IsDeleted = x.IsDeleted,
                AssignedToProjects = x.AssignedToProjects.Where(u => !u.IsDeleted)
                .Select(y => new AssignedUserToProject()
                {
                    AssignedByUser = y.AssignedByUser,
                    Project = y.Project,
                    ProjectId = y.ProjectId
                }).ToList(),
                Id = x.Id
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAllIdpUserIdsIncludingDeletedAsync(CancellationToken ct) => await DbSet
            .Select(x => x.IdentityProviderUserId)
            .ToListAsync(ct);

    public async Task<User> GetBySidWithProjectsAsync(string idpUserId, CancellationToken ct) => await DbSet.Where(x => x.IdentityProviderUserId == idpUserId)
            .Include(x => x.AssignedProvider)
            .Include(x => x.AssignedToProjects)
            .ThenInclude(x => x.Project)
            .ThenInclude(x => x.UserPermissions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
    public async Task<User> GetBySidWithProviderAsync(string idpUserId, CancellationToken ct) => await DbSet.Where(x => x.IdentityProviderUserId == idpUserId)
            .Include(x => x.AssignedProvider)
            .FirstOrDefaultAsync(ct);

    public async Task<IList<WeekCount>> CreatedFrom(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var weeks = WeekPart.GetWeeksFromStartDate(fromDate, toDate);
        var limitDateFrom = fromDate.ToDateTime(TimeOnly.MinValue);
        var limitDateTo = toDate.ToDateTime(TimeOnly.MaxValue);
        var query = await DbSet
            .Where(x => !x.IsDeleted && x.CreatedAt >= limitDateFrom && x.CreatedAt <= limitDateTo)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { Idx = WeekPart.GetWeekPartForDate(weeks, x.CreatedAt), Data = x })
            .ToListAsync(ct)
            ;

        return query.GroupBy(g => g.Idx)
            .Select(x => new WeekCount(DateOnly.FromDateTime(x.Key.StartDate), DateOnly.FromDateTime(x.Key.EndDate), x.Count()))
            .ToList();
    }

    public async Task<int> TotalCount(CancellationToken ct) => await DbSet.CountAsync(x => !x.IsDeleted, ct)
            ;


    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct) => await DbSet.Where(x => x.UserName == username && x.IsDeleted == false)
            .FirstOrDefaultAsync(ct);


    public async Task<Guid?> GetUserIdByIdpUserIdAsync(string idpUserId, CancellationToken ct)
    {
        var userId = await DbSet.Where(x => x.IdentityProviderUserId == idpUserId && x.IsDeleted == false)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

        return userId == Guid.Empty ? null : userId;
    }

    public async Task<Guid?> GetProviderIdByIdpUserIdAsync(string idpUserId, CancellationToken ct)
    {
        var provider = await DbSet.Where(x => x.IdentityProviderUserId == idpUserId && x.IsDeleted == false)
            .Select(x => x.AssignedProvider)
            .FirstOrDefaultAsync(ct);

        return provider is null ? null : provider.Id;
    }
}