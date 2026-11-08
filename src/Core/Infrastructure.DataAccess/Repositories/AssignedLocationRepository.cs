using FluentValidation;

using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class AssignedLocationRepository(DatabaseContext context) : GenericRepository<AssignedLocation, Guid>(context), IAssignedLocationRepository
{
    public override void Delete(AssignedLocation entityToDelete)
    {
        AssignedLocation assignedLocation = DbSet
            .First(f => f.Id == entityToDelete.Id);

        DbSet.Remove(assignedLocation);
    }

    public override void Delete(Guid id)
    {
        AssignedLocation assignedLocation = DbSet
            .First(f => f.Id == id);

        DbSet.Remove(assignedLocation);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancelationToken)
    {
        AssignedLocation assignedLocation = await DbSet
            .FirstAsync(f => f.Id == id, cancelationToken)
            ;

        DbSet.Remove(assignedLocation);
    }

    public async Task<IList<AssignedLocation>> GetAllBySpecificationsAsync(Guid? ProjectResourceId, Guid? ProviderId, CancellationToken ct = default)
    {
        IQueryable<AssignedLocation> projectResources = DbSet
            .AsSplitQuery()
            .Include(x => x.AssignedLocationsValues)
            .Include(x => x.Location)
                .ThenInclude(x => x.AssignedLocations)
                .ThenInclude(x => x.AssignedLocationsValues)
            .Include(i => i.Location)
                .ThenInclude(i => i.LocationType)
             .Include(i => i.Location)
                .ThenInclude(i => i.Provider)
            .Include(i => i.AllocationUnitType)
            .Include(i => i.ProjectResource)
            .Where(w => !w.IsDeleted);

        if (ProjectResourceId is not null)
        {
            projectResources = projectResources.Where(w => w.ProjectResource.Id == ProjectResourceId);
        }

        if (ProviderId is not null)
        {
            projectResources = projectResources.Where(w => w.Location.Provider.Id == ProviderId && !w.Location.Provider.IsDeleted);
        }

        return await projectResources.ToListAsync(ct);
    }
}