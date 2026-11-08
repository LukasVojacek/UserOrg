using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IOtherService
{
    Task<IList<CountryModel>> GetCountriesAsync(string? countryCode, CancellationToken ct);
    Task<StatisticsModel> GetWeeklyStatistics(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}