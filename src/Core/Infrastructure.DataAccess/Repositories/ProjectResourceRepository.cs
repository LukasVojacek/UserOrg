using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class ProjectResourceRepository(DatabaseContext context) : GenericRepository<ProjectResource, Guid>(context), IProjectResourceRepository
{
    public override void Delete(ProjectResource entityToDelete)
    {
        ProjectResource projectResource = DbSet
            .Include(i => i.AssignedLocations)
            .First(f => f.Id == entityToDelete.Id);

        Context.AssignedLocations.RemoveRange(projectResource.AssignedLocations);
        DbSet.Remove(projectResource);
    }

    public override void Delete(Guid id)
    {
        ProjectResource projectResource = DbSet
            .Include(i => i.AssignedLocations)
            .First(f => f.Id == id);

        Context.AssignedLocations.RemoveRange(projectResource.AssignedLocations);
        DbSet.Remove(projectResource);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancelationToken)
    {
        ProjectResource projectResource = await DbSet
            .Include(i => i.AssignedLocations)
            .FirstAsync(f => f.Id == id, cancelationToken);

        Context.AssignedLocations.RemoveRange(projectResource.AssignedLocations);
        DbSet.Remove(projectResource);
    }

    public async Task<ProjectResource> GetByIdWithIncludesAsync(Guid? projectResourceId, CancellationToken ct)
    {
        ProjectResource projectResource = await DbSet
            .Include(i => i.AssignedLocations).ThenInclude(i => i.AllocationUnitType)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.Location).ThenInclude(i => i.LocationType)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.Location).ThenInclude(i => i.Provider)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.AssignedLocationsValues)
            .Include(i => i.AssignedProjects).ThenInclude(i => i.Project)
            .AsSplitQuery()
            .Where(w => !w.IsDeleted)
            .FirstOrDefaultAsync(f => f.Id == projectResourceId, ct)
            ;

        return projectResource;
    }
    public async Task<ProjectResource?> GetByNameWithIncludesAsync(string projectResourceName, CancellationToken ct)
    {
        ProjectResource projectResource = await DbSet
            .Include(i => i.AssignedLocations).ThenInclude(i => i.AllocationUnitType)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.Location).ThenInclude(i => i.LocationType)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.Location).ThenInclude(i => i.Provider)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.AssignedLocationsValues)
            .AsSplitQuery()
            .Where(w => !w.IsDeleted)
            .FirstOrDefaultAsync(f => EF.Functions.Like(f.Name, projectResourceName), ct)
            ;

        return projectResource;
    }

    public async Task<IList<ProjectResource>> GetAllBySpecificationsAsync(Guid? providerId, ProjectId? projectId, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct)
    {
        IQueryable<ProjectResource> projectResources = DbSet
            .Include(i => i.AssignedLocations).ThenInclude(i => i.AllocationUnitType)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.Location).ThenInclude(i => i.LocationType)
            .Include(i => i.AssignedLocations).ThenInclude(i => i.AssignedLocationsValues)
            .AsSplitQuery()
            .Where(w => !w.IsDeleted).AsQueryable();
        if (providerId.HasValue && providerId.Value != Guid.Empty)
        {
            projectResources = projectResources.Where(w => w.AssignedLocations.Any(x => x.Location.Provider.Id == providerId.Value)).AsQueryable();
        }

        var filters = new List<Expression<Func<ProjectResource, bool>>>();

        Expression<Func<ProjectResource, bool>> userPredicate = (startDate, endDate) switch
        {
            (null, DateTime) => w => w.EndDate <= endDate,
            (DateTime, null) => w => w.StartDate >= startDate,
            (DateTime, DateTime) => w => w.StartDate >= startDate && w.EndDate <= endDate,
            _ => w => true
        };

        filters.Add(userPredicate);

        if (projectId is not null)
        {
            filters.Add(w => w.AssignedProjects.Any(we => we.ProjectId == projectId));
        }

        if (locationTypeId is not null)
        {
            filters.Add(w => w.AssignedLocations.Any(a => a.Location.LocationType.Id == locationTypeId));
        }

        filters.ForEach(f => projectResources = projectResources.Where(f));
        return await projectResources.ToListAsync(ct);
    }
    public async Task<IList<ProjectResource>> GetAllBySpecificationsAsync(Guid? providerId, ProjectShortName? projectShortName, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct)
    {
        IQueryable<ProjectResource> projectResources = DbSet
            .Include(i => i.AssignedLocations.Where(w => !w.IsDeleted))
                .ThenInclude(i => i.AllocationUnitType)
            .Include(i => i.AssignedLocations.Where(w => !w.IsDeleted))
                .ThenInclude(i => i.Location)
                    .ThenInclude(i => i.LocationType)
            .Include(i => i.AssignedLocations.Where(w => !w.IsDeleted))
                .ThenInclude(i => i.AssignedLocationsValues)
            .Include(i => i.AssignedProjects.Where(w => !w.IsDeleted))
                .ThenInclude(i => i.Project)
            .AsSplitQuery()
            .Where(w => !w.IsDeleted)
            .AsQueryable();

        if (providerId.HasValue && providerId.Value != Guid.Empty)
        {
            projectResources = projectResources.Where(w => w.AssignedLocations.Any(x => x.Location.Provider.Id == providerId.Value)).AsQueryable();
        }

        projectResources = projectResources.Where((startDate, endDate) switch
        {
            (null, DateTime) => w => w.EndDate <= endDate,
            (DateTime, null) => w => w.StartDate >= startDate,
            (DateTime, DateTime) => w => w.StartDate >= startDate && w.EndDate <= endDate,
            _ => w => true
        }).AsQueryable();

        if (projectShortName is not null)
        {
            projectResources = projectResources.Where(w => w.AssignedProjects.Any(we => !we.IsDeleted && we.Project.ShortName == projectShortName))
                .AsQueryable();

        }

        if (locationTypeId is not null)
        {
            projectResources = projectResources.Where(w => w.AssignedLocations.Any(a => !a.IsDeleted && a.Location.LocationType.Id == locationTypeId))
                .AsQueryable();
        }

        return await projectResources.ToListAsync(ct);
    }

    public async Task<IList<ProjectResource>> GetAllForServiceAsync(CancellationToken ct)
    {
        List<ProjectResource> data = await DbSet.AsNoTracking()
          .Include(x => x.AssignedLocations).ThenInclude(x => x.Location).ThenInclude(x => x.LocationType)
          .Include(x => x.AssignedLocations).ThenInclude(x => x.Location).ThenInclude(x => x.Provider)
          .Include(x => x.AssignedLocations).ThenInclude(x => x.AllocationUnitType)
          .Include(x => x.AssignedLocations).ThenInclude(x => x.AssignedLocationsValues)
          .Include(x => x.AssignedProjects).ThenInclude(x => x.Project)
          .AsSplitQuery()
          .Where(x => !x.IsDeleted)
          .ToListAsync(ct);

        return data;
    }

    public async Task<IList<WeekCount>> CreatedFrom(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        IList<WeekPart> weeks = WeekPart.GetWeeksFromStartDate(fromDate, toDate);
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
}