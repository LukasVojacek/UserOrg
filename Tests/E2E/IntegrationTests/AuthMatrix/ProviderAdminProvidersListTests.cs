using System.Net;
using System.Text.Json;

using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests.AuthMatrix;

[Collection(ParityTestCollection.Name)]
public sealed class ProviderAdminProvidersListTests(GatewayAttachFixture fixture)
{
    private const string Route = "/api/v2/ProviderAdmin/Providers";

    [Fact]
    public async Task Should_ListOnlyTheOwnedProviderWithLocations_When_ProviderAdminListsWithoutIncludeInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateProviderAdminGatewayClient();

        using var response = await client.GetAsync(new Uri(Route, UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {Route} as ProviderAdmin: expected 200, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        using var document = JsonDocument.Parse(body);
        var only = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(GatewayAttachFixture.OwnedProviderName, only.GetProperty("Name").GetString());
        Assert.NotEqual(0, only.GetProperty("Locations").GetArrayLength());
        Assert.DoesNotContain(GatewayAttachFixture.ForeignProviderName, Names(document));
    }

    [Fact]
    public async Task Should_ListTheForeignProvider_When_AdminLists()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();

        using var response = await client.GetAsync(new Uri($"{Route}?limit=300", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {Route} as Admin: expected 200, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        using var document = JsonDocument.Parse(body);
        var names = Names(document);
        Assert.Contains(GatewayAttachFixture.ForeignProviderName, names);
        Assert.Contains(GatewayAttachFixture.OwnedProviderName, names);
    }

    [Fact]
    public async Task Should_ListTheOwnedProvider_When_AdminListsWithIncludeInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();

        using var response = await client.GetAsync(new Uri($"{Route}?includeInactive=true&limit=300", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {Route}?includeInactive=true as Admin: expected 200, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        using var document = JsonDocument.Parse(body);
        Assert.Contains(GatewayAttachFixture.OwnedProviderName, Names(document));
    }

    [Fact]
    public async Task Should_ReturnBadRequest_When_IncludeInactiveIsMalformed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();

        using var response = await client.GetAsync(new Uri($"{Route}?includeInactive=maybe", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"GET {Route}?includeInactive=maybe: expected 400, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        Assert.Contains("validation.invalid_format", body, StringComparison.Ordinal);
    }

    private static List<string?> Names(JsonDocument document) =>
        [.. document.RootElement.EnumerateArray().Select(p => p.GetProperty("Name").GetString())];
}