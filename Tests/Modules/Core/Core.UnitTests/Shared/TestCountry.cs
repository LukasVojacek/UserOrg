namespace Core.UnitTests.Shared;

/// <summary>
/// Builds a <see cref="Country"/> row for tests. Country is seeded reference data with a private constructor and
/// private setters, so the instance is created and filled by reflection.
/// </summary>
internal static class TestCountry
{
    internal static Country Create(int id = 1, string code = "CZ")
    {
        var country = (Country)Activator.CreateInstance(typeof(Country), nonPublic: true)!;
        typeof(Country).GetProperty(nameof(Country.Id))!.SetValue(country, id);
        typeof(Country).GetProperty(nameof(Country.Name))!.SetValue(country, new CountryName(code));
        return country;
    }
}
