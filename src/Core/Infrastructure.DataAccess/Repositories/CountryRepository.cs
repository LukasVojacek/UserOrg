using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class CountryRepository(DatabaseContext context) : GenericRepository<Country, int>(context), ICountryRepository
{
    public async Task<Country> GetByCountryCodeAsync(string code, CancellationToken ct) => await DbSet.FirstOrDefaultAsync(w => w.Name == code, ct);

    public async Task<IList<Country>> GetAllByCountryCodeAsync(string? code, CancellationToken ct) => string.IsNullOrEmpty(code) ? await DbSet.ToListAsync(ct) : await DbSet.Where(w => w.Name.Contains(code)).ToListAsync(ct);
}