using Core.Domain.Exceptions;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Provider;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;

namespace UserOrganizationManagement.Application.UserOrg.Services;
/// <summary>
/// Constructor
/// </summary>
/// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
public class ProviderService(IUserOrgUnitOfWork userOrgUnitOfWork) : IProviderService
{

    #region Constructors
    #endregion
    public async Task<IList<ProviderModel>> GetRelevantProvidersAsync(Guid userId, CancellationToken ct = default)
    {
        IList<Provider> providers = await userOrgUnitOfWork.ProviderRepository.GetRelevantProvidersAsync(userId, ct);

        return providers.Select(s => s.ConvertToModel()).ToList();
    }
    public async Task<IList<ProviderModel>> GetPublicProvidersAsync(CancellationToken ct = default)
    {
        IList<Provider> providers = await userOrgUnitOfWork.ProviderRepository.GetPublicProvidersAsync(ct);

        return providers.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<IList<ProviderModel>> GetProvidersAsync(string? countryCode, bool onlyPublic, CancellationToken ct = default)
    {
        IList<Provider> providers = await userOrgUnitOfWork.ProviderRepository.GetProvidersBySpecificationOfCountryAsync(countryCode, onlyPublic, ct);
        return countryCode is not null && providers.Count == 0
            ? throw new RecordNotFoundException($"""Providers for input parameter: CountryCode:"{countryCode}" do not exist!""")
            : (IList<ProviderModel>)providers.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<ProviderModel> GetProviderAsync(Guid id, bool onlyPublic, CancellationToken ct = default)
    {
        Provider provider = await userOrgUnitOfWork.ProviderRepository.GetByIdAsync(id, ct, i => i.Country);
        return provider is null
            ? throw new RecordNotFoundException($"""Provider with id:"{id}" does not exist!""")
            : (onlyPublic, provider.IsPrivate) switch
            {
                (true, false) => await Task.FromResult(provider.ConvertToModel()),
                (true, true) => throw new RecordNotFoundException($"""Provider with id:"{id}" does not exist!"""),
                _ => await Task.FromResult(provider.ConvertToModel()),
            };
    }

    public async Task<Guid> InsertProviderAsync(ProviderCreate obj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(obj);

        Country country = await userOrgUnitOfWork.CountryRepository.GetByCountryCodeAsync(obj.CountryCode!, ct)
            ?? throw new RecordNotFoundException($"""Country with code:"{obj.CountryCode}" does not exist!""");

        Provider createdProvider = obj.ConvertToEntity(country);
        await userOrgUnitOfWork.ProviderRepository.InsertAsync(createdProvider, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
        return createdProvider.Id;
    }

    public async Task UpdateProviderAsync(ProviderUpdate obj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(obj);

        Provider provider = await userOrgUnitOfWork.ProviderRepository.GetByIdAsync(obj.Id!.Value, ct, i => i.Country)
            ?? throw new RecordNotFoundException($"""Provider with id:"{obj.Id}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(provider.Mode);
        Country country = provider.Country;

        if (provider.Country.Name != obj.CountryCode)
        {
            country = await userOrgUnitOfWork.CountryRepository.GetByCountryCodeAsync(obj.CountryCode!, ct);
            if (country is null)
            {
                throw new RecordNotFoundException($"""Country with code:"{obj.CountryCode}" does not exist!""");
            }
        }

        UpdateEntity(provider, obj, country);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken ct)
    {
        Provider provider = await userOrgUnitOfWork.ProviderRepository.GetByIdAsync(id, ct)
            ?? throw new RecordNotFoundException($"""Provider with id:"{id}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(provider.Mode);

        await userOrgUnitOfWork.ProviderRepository.DeleteAsync(provider.Id, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    private static void UpdateEntity(Provider resProvider, ProviderUpdate obj, Country country)
    {
        resProvider.Name = obj.Name;
        resProvider.Description = obj.Description;
        resProvider.IsPrivate = obj.IsPrivate;
        resProvider.Country = country;
        resProvider.SupportEmail = obj.SupportEmail;
    }
}