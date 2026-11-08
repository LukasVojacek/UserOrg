using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IProviderRepository : IRepository<Provider, Guid>
{
    Task<IList<Provider>> GetProvidersBySpecificationOfCountryAsync(string? countryCode, bool onlyPublic = true, CancellationToken ct = default);
    Task<IList<Provider>> GetRelevantProvidersAsync(Guid userId, CancellationToken ct = default);
    Task<IList<Provider>> GetPublicProvidersAsync(CancellationToken ct = default);
}