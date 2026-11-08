using System.Net.Http.Json;
using System.Text.Json;

using E2E.Fixtures;

namespace IntegrationTests.Writes;

/// <summary>
/// v2 location create through the Gateway: the POST carries the location type, the location is read back
/// through the v2 GET, and the stored type is the one requested.
/// </summary>
/// <remarks>
/// Resets to the seed baseline after each test (<see cref="DisposeAsync"/>) so the new location never leaks
/// into another spec sharing this assembly's <see cref="GatewayAttachFixture"/> instance.
/// </remarks>
[Collection(ParityTests.ParityTestCollection.Name)]
public sealed class AddLocationWriteTests(GatewayAttachFixture fixture) : IAsyncLifetime
{
    private const string SeededProviderName = "prov1";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await fixture.ResetToSeedAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_StoreRequestedLocationType_When_AdminAddsLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();
        const string locationName = "e2e-addloc-hpc";

        using var response = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new
            {
                Name = locationName,
                Endpoint = "https://hpc.example.test",
                LocationType = "HPC",
                IsPrivate = false,
                IsShared = true,
                Description = "Created by AddLocationWriteTests.",
            },
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 for location create, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var readBack = await admin.GetAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{locationName}", UriKind.Relative),
            ct);
        var readBackBody = await readBack.Content.ReadAsStringAsync(ct);

        Assert.True(
            readBack.StatusCode == HttpStatusCode.OK,
            $"Expected 200 reading the created location back, got {(int)readBack.StatusCode}.{Environment.NewLine}{readBackBody}");

        using var document = JsonDocument.Parse(readBackBody);
        Assert.Equal(locationName, document.RootElement.GetProperty("Name").GetString());
        Assert.Equal("HPC", document.RootElement.GetProperty("LocationType").GetString());
    }
}
