using Core.Domain.Exceptions;

using UserOrg.Shared.Extensions;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;

namespace UserOrganizationManagement.Application.UserOrg.Services;

/// <summary>
/// Constructor
/// </summary>
/// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
public class LocationService(IUserOrgUnitOfWork userOrgUnitOfWork) : ILocationService
{

    #region Constructors
    #endregion
    public async Task<IList<LocationModel>> GetLocationsAsync(string? providerName, int? locationTypeId, bool onlyPublic, CancellationToken ct)
    {
        IList<Location> locations = await userOrgUnitOfWork.LocationRepository.GetLocationsBySpecificationOfProviderNameAndLocationTypeAsync(providerName, locationTypeId, onlyPublic, ct);
        if ((providerName is not null || locationTypeId is not null) && locations.Count == 0)
        {
            var provName = string.IsNullOrEmpty(providerName) ? string.Empty : $"providerName:{providerName}";
            var locTypeId = locationTypeId is null ? string.Empty : $"locationTypeId:{locationTypeId}";
            throw new RecordNotFoundException($"""Location for input parameters: {provName} {locTypeId} do not exist!""");
        }

        return locations.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<IList<LocationModel>> GetRelatedLocationAsync(string? idpUserId, Guid? providerKey, string? providerName, int? locationTypeId, CancellationToken ct)
    {
        IEnumerable<Location> locations = await userOrgUnitOfWork.LocationRepository.GetLocationsForUserOrProviderAsync(idpUserId, providerKey, ct);
        if (!string.IsNullOrEmpty(providerName))
        {
            locations = locations.Where(w => w.Provider.Name.Contains(providerName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (locationTypeId is not null)
        {
            locations = locations.Where(w => w.LocationType.Id == locationTypeId).ToList();
        }

        return locations.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<LocationModel> GetLocationAsync(Guid id, bool onlyPublic, CancellationToken ct)
    {
        Location location = await userOrgUnitOfWork.LocationRepository.GetByIdAsync(id, ct, i => i.LocationType, i => i.Provider, i => i.Provider.Country, i => i.Provider.Country);
        return location is null
                ? throw new RecordNotFoundException($"""Location with id:"{id}" does not exist!""")
                : (onlyPublic, location.IsPrivate) switch
                {
                    (true, false) => await Task.FromResult(location.ConvertToModel()),
                    (true, true) => throw new RecordNotFoundException($"""Location with id:"{id}" does not exist!"""),
                    _ => await Task.FromResult(location.ConvertToModel()),
                };
    }

    public async Task<Guid> InsertLocationAsync(LocationCreate obj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(obj);
        Provider provider = await userOrgUnitOfWork.ProviderRepository.GetByIdAsync(obj.ProviderId!.Value, ct)
            ?? throw new RecordNotFoundException($"""Provider with id:"{obj.ProviderId}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(provider.Mode);

        LocationType locType = await userOrgUnitOfWork.LocationTypeRepository.GetByIdAsync(obj.LocationTypeId!.Value, ct)
            ?? throw new RecordNotFoundException($"""Location type with id:"{obj.LocationTypeId}" does not exist!""");

        Location createdLocation = obj.ConvertToEntity(provider, locType);
        await userOrgUnitOfWork.LocationRepository.InsertAsync(createdLocation, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
        return createdLocation.Id;
    }

    public async Task UpdateLocationAsync(LocationUpdate obj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(obj);
        Location location = await userOrgUnitOfWork.LocationRepository.GetByIdAsync(obj.Id!.Value, ct, i => i.LocationType, i => i.Provider)
            ?? throw new RecordNotFoundException($"""Location with id:"{obj.Id}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(location.Mode);
        Provider provider = location.Provider;
        RecordReadOnlyException.ThrowIfReadOnly(provider.Mode);
        LocationType locationType = location.LocationType;

        if (provider.Id != obj.ProviderId)
        {
            provider = await userOrgUnitOfWork.ProviderRepository.GetByIdAsync(obj.ProviderId!.Value, ct);
            if (provider is null)
            {
                throw new RecordNotFoundException($"""Provider with id:"{obj.ProviderId}" does not exist!""");
            }
        }

        if (locationType.Id != obj.LocationTypeId)
        {
            locationType = await userOrgUnitOfWork.LocationTypeRepository.GetByIdAsync(obj.LocationTypeId!.Value, ct);
            if (locationType is null)
            {
                throw new RecordNotFoundException($"""Location type with id:"{obj.LocationTypeId}" does not exist!""");
            }
        }

        UpdateEntity(location, obj, provider, locationType);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    public async Task DeleteLocationAsync(Guid id, CancellationToken ct)
    {
        Location location = await userOrgUnitOfWork.LocationRepository.GetByIdAsync(id, ct)
            ?? throw new RecordNotFoundException($"""Location with id:"{id}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(location.Mode);

        await userOrgUnitOfWork.LocationRepository.DeleteAsync(location.Id, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    public async Task<IList<Guid>> GetLocationIdsForProviderIdAsync(Guid providerId, CancellationToken ct)
    {
        IList<Guid> locations = await userOrgUnitOfWork.LocationRepository.GetLocationIdsForProviderId(providerId, ct);
        return locations;
    }

    private static void UpdateEntity(Location location, LocationUpdate obj, Provider resProvider, LocationType type)
    {
        location.Name = obj.Name;
        location.Description = obj.Description;
        location.IsPrivate = obj.IsPrivate;
        location.Endpoint = obj.Endpoint;
        location.Provider = resProvider;
        location.LocationType = type;
        location.IsShared = obj.IsShared;
        location.DefaultServiceIntegrationUrl = string.IsNullOrEmpty(obj.DefaultServiceIntegrationUrl)
            ? null
            : obj.DefaultServiceIntegrationUrl.ToNormalizeUrl();
    }
}