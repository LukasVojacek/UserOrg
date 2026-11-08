using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared.Extensions;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class LocationRepository(DatabaseContext context) : GenericRepository<Location, Guid>(context), ILocationRepository
{
    public override void Delete(Location entityToDelete)
    {
        var location = DbSet
            .Include(i => i.AssignedLocations)
            .First(f => f.Id == entityToDelete.Id);

        Context.AssignedLocations.RemoveRange(location.AssignedLocations);

        DbSet.Remove(location);
    }

    public override void Delete(Guid id)
    {
        var location = DbSet
            .Include(i => i.AssignedLocations)
            .First(f => f.Id == id);

        Context.AssignedLocations.RemoveRange(location.AssignedLocations);

        DbSet.Remove(location);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancelationToken)
    {
        var location = await DbSet
            .Include(i => i.AssignedLocations)
            .FirstAsync(f => f.Id == id, cancelationToken)
            ;

        Context.AssignedLocations.RemoveRange(location.AssignedLocations);

        DbSet.Remove(location);
    }

    public async Task<Location?> GetByIdWithAssignmentsAsync(Guid locationId, CancellationToken ct = default) => await DbSet
            .Include(i => i.AssignedLocations)
            .Include(i => i.Provider)
            .FirstOrDefaultAsync(x => x.Id == locationId && !x.IsDeleted, ct)
            ;

    public async Task<IList<Guid>> GetLocationIdsForProviderId(Guid providerId, CancellationToken ct = default) => await DbSet.Where(x => x.Provider.Id == providerId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(ct);

    public async Task<IList<Location>> GetLocationsBySpecificationOfProviderNameAndLocationTypeAsync(string? providerName, int? locationTypeId, bool onlyPublic = true, CancellationToken ct = default)
    {
        var locations = DbSet
           .Include(i => i.LocationType)
           .Include(i => i.Provider)
             .ThenInclude(i => i.Country)
           .AsSplitQuery()
           .AsQueryable();

        var filters = new List<Expression<Func<Location, bool>>>
        {
            w => !w.IsDeleted
        };

        if (onlyPublic)
        {
            filters.Add(w => !w.IsPrivate);
        }

        if (!string.IsNullOrEmpty(providerName))
        {
            filters.Add(w => w.Provider.Name.Contains(providerName));
        }

        if (locationTypeId is not null)
        {
            filters.Add(w => w.LocationType.Id == locationTypeId);
        }

        filters.ForEach(f => locations = locations.Where(f));
        return await locations.ToListAsync(ct);
    }

    public async Task<IEnumerable<Location>> GetLocationsForUserOrProviderAsync(string? idpUserId, Guid? providerKey, CancellationToken ct = default)
    {
        if (idpUserId.IsNullOrEmptyOrEmptyGuid() && providerKey.IsNullOrEmpty())
        {
            return [];
        }

        if (!providerKey.IsNullOrEmpty() && providerKey != Guid.Empty)
        {
            var query = await DbSet
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Provider)
                .Include(x => x.Provider).ThenInclude(x => x.Country)
                .Include(x => x.LocationType)
                .Where(l => l.Provider.Id == providerKey.Value && !l.IsDeleted)
                .ToListAsync(ct);
            return query;
        }
        else
        {
            var locationIds = await Context.Set<User>()
                .Where(x => x.IdentityProviderUserId == idpUserId)
                .SelectMany(x => x.AssignedToProjects)
                .Select(x => x.Project)
                .SelectMany(x => x.AssignedProjectResources)
                .SelectMany(x => x.ProjectResource.AssignedLocations)
                .Select(x => x.Location.Id)
                .ToListAsync(ct)
                ;

            var query = await DbSet.Where(l => locationIds.Contains(l.Id) && !l.IsDeleted)
                .Include(x => x.Provider).ThenInclude(x => x.Country)
                .Include(x => x.LocationType)
                .AsSplitQuery()
                .ToListAsync(ct)
                ;

            return query;
        }
    }
}