using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface ICountryRepository : IRepository<Country, int>
{
    Task<Country> GetByCountryCodeAsync(string code, CancellationToken ct);
    Task<IList<Country>> GetAllByCountryCodeAsync(string? code, CancellationToken ct);
}