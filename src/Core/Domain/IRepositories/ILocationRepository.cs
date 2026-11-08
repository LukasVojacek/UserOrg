using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface ILocationRepository : IRepository<Location, Guid>
{
    Task<IList<Location>> GetLocationsBySpecificationOfProviderNameAndLocationTypeAsync(string? providerName, int? locationTypeId, bool onlyPublic = true, CancellationToken ct = default);
    Task<IList<Guid>> GetLocationIdsForProviderId(Guid providerId, CancellationToken ct = default);
    Task<IEnumerable<Location>> GetLocationsForUserOrProviderAsync(string? idpUserId, Guid? providerKey, CancellationToken ct = default);
    Task<Location?> GetByIdWithAssignmentsAsync(Guid locationId, CancellationToken ct = default);
}