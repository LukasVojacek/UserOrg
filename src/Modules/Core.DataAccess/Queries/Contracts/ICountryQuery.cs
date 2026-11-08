namespace Core.DataAccess.Queries.Contracts;

public interface ICountryQuery
{
    Task<List<CountrySummary>> GetCountriesAsync(string? countryCode, CancellationToken ct);

    /// <summary>
    /// Resolves a country code to its lookup id, or null when no country matches. "Code" is physically
    /// the <c>Country.Name</c> column (legacy mapping), matched exactly (case-insensitive via collation),
    /// mirroring the legacy <c>CountryRepository.GetByCountryCodeAsync</c>.
    /// </summary>
    Task<int?> FindIdByCodeAsync(string code, CancellationToken ct);
}