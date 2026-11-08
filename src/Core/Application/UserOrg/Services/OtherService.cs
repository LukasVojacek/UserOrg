using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

namespace UserOrganizationManagement.Application.UserOrg.Services;

/// <summary>
/// Constructor
/// </summary>
/// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
public class OtherService(IUserOrgUnitOfWork userOrgUnitOfWork) : IOtherService
{

    #region Constructors
    #endregion
    public async Task<IList<CountryModel>> GetCountriesAsync(string? countryCode, CancellationToken ct)
    {
        IList<Country> countries = await userOrgUnitOfWork.CountryRepository.GetAllByCountryCodeAsync(countryCode, ct);
        return countryCode is not null && countries.Count == 0
            ? throw new RecordNotFoundException($"""Countries for input parameters: CountryCode:"{countryCode}" do not exist!""")
            : (IList<CountryModel>)countries.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<StatisticsModel> GetWeeklyStatistics(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        StatisticsModel data = await userOrgUnitOfWork.GetStatisticsFrom(fromDate, toDate, cancellationToken);

        return data ?? StatisticsModel.Empty;
    }
}