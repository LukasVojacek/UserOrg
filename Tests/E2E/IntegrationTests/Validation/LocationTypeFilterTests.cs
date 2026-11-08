using System.Text.Json;

using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests.Validation;

/// <summary>
/// The v2 <c>?locationType=</c> filters through the Gateway: the name binds to the domain enum, the rows carry
/// the type by name, <c>Unspecified</c> matches nothing and an unknown name fails binding.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class LocationTypeFilterTests(GatewayAttachFixture fixture)
{
    private const string SeededProviderName = "prov1";

    [Fact]
    public async Task Should_ReturnOnlyHpcLocations_When_LocationsFilteredByHpc()
    {
        using var items = await GetArrayAsync("/api/v2/Locations?locationType=HPC");

        Assert.NotEmpty(items.RootElement.EnumerateArray());
        Assert.All(items.RootElement.EnumerateArray(), item => Assert.Equal("HPC", item.GetProperty("LocationType").GetString()));
    }

    [Fact]
    public async Task Should_ReturnEmptyList_When_LocationsFilteredByUnspecified()
    {
        using var all = await GetArrayAsync("/api/v2/Locations");
        using var unspecified = await GetArrayAsync("/api/v2/Locations?locationType=Unspecified");

        Assert.NotEmpty(all.RootElement.EnumerateArray());
        Assert.Empty(unspecified.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Should_ReturnInvalidFormat_When_LocationsFilteredByUnknownName()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.GetAsync(new Uri("/api/v2/Locations?locationType=Foo", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 400 for an unknown location type name, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        using var document = JsonDocument.Parse(body);
        Assert.Equal("validation.invalid_format", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Should_ReturnOnlyResourcesWithHpcAssignments_When_ResourcesFilteredByHpc()
    {
        using var items = await GetArrayAsync($"/api/v2/Providers/{SeededProviderName}/Resources?locationType=HPC");

        Assert.NotEmpty(items.RootElement.EnumerateArray());
        Assert.All(
            items.RootElement.EnumerateArray(),
            item => Assert.Contains(
                item.GetProperty("Assignments").EnumerateArray(),
                assignment => assignment.GetProperty("LocationType").GetString() == "HPC"));
    }

    [Fact]
    public async Task Should_ReturnEmptyList_When_ResourcesFilteredByUnspecified()
    {
        using var all = await GetArrayAsync($"/api/v2/Providers/{SeededProviderName}/Resources");
        using var unspecified = await GetArrayAsync($"/api/v2/Providers/{SeededProviderName}/Resources?locationType=Unspecified");

        Assert.NotEmpty(all.RootElement.EnumerateArray());
        Assert.Empty(unspecified.RootElement.EnumerateArray());
    }

    private async Task<JsonDocument> GetArrayAsync(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();

        using var response = await admin.GetAsync(new Uri(path, UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 for {path}, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        return document;
    }
}
