using Core.DataAccess.Queries.Contracts;

namespace Core.DataAccess.Queries;

internal sealed class CountryQuery(IDataQuery<Country> countryQuery) : ICountryQuery
{
    public async Task<List<CountrySummary>> GetCountriesAsync(string? countryCode, CancellationToken ct)
    {
        // Country is a small seeded reference table; project everything in SQL (Name -> Code,
        // Description -> Name, matching the legacy mapping) and apply the substring filter in
        // memory. The legacy filter is a substring match on the Name column (exposed as Code).
        var all = await countryQuery.ToListAsync(
            countries => countries.Select(c => new CountrySummary(c.Name.Value, c.Description)),
            "GetCountries",
            ct);

        return countryCode is { Length: > 0 } code
            ? all.Where(c => c.Code.Contains(code, StringComparison.OrdinalIgnoreCase)).ToList()
            : all;
    }

    public Task<int?> FindIdByCodeAsync(string code, CancellationToken ct)
    {
        // Compare the value object itself, not its .Value: Name is value-converted, so `c.Name.Value == code`
        // has no SQL translation and throws at runtime (in-memory providers hide this). Built with the raw
        // constructor rather than CountryName.From so an out-of-range code is a miss, not an exception.
        var name = new CountryName(code);

        // Exact match on the Name column (exposed as Code), case-insensitive via SQL collation —
        // the same lookup the legacy CountryRepository.GetByCountryCodeAsync performed.
        return countryQuery.FirstOrDefaultAsync(
            countries => countries.Where(c => c.Name == name).Select(c => (int?)c.Id),
            "FindCountryIdByCode",
            ct);
    }
}