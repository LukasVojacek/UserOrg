using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class ProviderRepository(DatabaseContext context) : GenericRepository<Provider, Guid>(context), IProviderRepository
{
    public override void Delete(Provider entityToDelete)
    {
        Provider provider = DbSet
            .Include(i => i.Locations)
                .ThenInclude(i => i.AssignedLocations)
            .First(f => f.Id == entityToDelete.Id);

        Context.Locations.RemoveRange(provider.Locations);
        Context.AssignedLocations.RemoveRange(provider.Locations.SelectMany(s => s.AssignedLocations));

        DbSet.Remove(provider);
    }

    public override void Delete(Guid id)
    {
        Provider provider = DbSet
            .Include(i => i.Locations)
                .ThenInclude(i => i.AssignedLocations)
            .First(f => f.Id == id);

        Context.Locations.RemoveRange(provider.Locations);
        Context.AssignedLocations.RemoveRange(provider.Locations.SelectMany(s => s.AssignedLocations));

        DbSet.Remove(provider);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancelationToken)
    {
        Provider provider = await DbSet
            .Include(i => i.Locations)
                .ThenInclude(i => i.AssignedLocations)
            .FirstAsync(f => f.Id == id, cancelationToken);

        Context.Locations.RemoveRange(provider.Locations);
        Context.AssignedLocations.RemoveRange(provider.Locations.SelectMany(s => s.AssignedLocations));

        DbSet.Remove(provider);
    }

    public async Task<IList<Provider>> GetProvidersBySpecificationOfCountryAsync(string? countryCode, bool onlyPublic = true, CancellationToken ct = default)
    {
        IQueryable<Provider> query = DbSet.Include(i => i.Country).AsQueryable();
        if (onlyPublic)
        {
            query = query.Where(w => !w.IsPrivate).AsQueryable();
        }

        IQueryable<Provider> providers = countryCode switch
        {
            null => query.Where(w => !w.IsDeleted),
            string => query.Where(w => !w.IsDeleted && w.Country.Name.Contains(countryCode)),
        };
        return await providers.ToListAsync(ct);
    }
    public async Task<IList<Provider>> GetRelevantProvidersAsync(Guid userId, CancellationToken ct = default)
    {
        List<Guid> providerIds = await Context.Projects
            .Where(x => x.AssignedUsers.Any(u => u.User.Id == userId) && x.AssignedProjectResources != null)
            .SelectMany(x => x.AssignedProjectResources)
            .Where(x => x.ProjectResource != null && x.ProjectResource.AssignedLocations != null)
            .Select(x => x.ProjectResource)
            .SelectMany(x => x.AssignedLocations)
            .Where(x => x.Location != null && x.Location.Provider != null)
            .Select(x => x.Location)
            .Select(x => x.Provider.Id)
            .Distinct()
            .ToListAsync(ct)
            ;

        return await DbSet.Where(x => providerIds.Contains(x.Id))
            .Include(x => x.Country)
            .ToListAsync(ct);

    }

    public async Task<IList<Provider>> GetPublicProvidersAsync(CancellationToken ct = default) => await DbSet
            .Include(x => x.Country)
            .Where(x => !x.IsPrivate && !x.IsDeleted)
            .Distinct()
            .ToListAsync(ct)
            ;
}