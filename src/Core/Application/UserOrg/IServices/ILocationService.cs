using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface ILocationService
{
    Task<IList<LocationModel>> GetLocationsAsync(string? providerName, int? locationTypeId, bool onlyPublic, CancellationToken ct);
    Task<IList<LocationModel>> GetRelatedLocationAsync(string? idpUserId, Guid? providerKey, string? providerName, int? locationTypeId, CancellationToken ct);
    Task<LocationModel> GetLocationAsync(Guid id, bool onlyPublic, CancellationToken ct);
    Task<Guid> InsertLocationAsync(LocationCreate obj, CancellationToken ct);
    Task UpdateLocationAsync(LocationUpdate obj, CancellationToken ct);
    Task DeleteLocationAsync(Guid id, CancellationToken ct);
    Task<IList<Guid>> GetLocationIdsForProviderIdAsync(Guid providerId, CancellationToken ct);
}