namespace IntegrationTests.HostComposition;

/// <summary>
/// Characterization tests pinning Core API host-composition-root behaviour (Program.cs) — ported from
/// the retired Aspire-backed <c>EndpointsTests</c> project onto the Testcontainers topology. Asserts
/// observable, end-to-end HTTP behaviour through the real Gateway + Core API so a dropped/renamed
/// registration goes red here, regardless of which extension method owns the call.
/// </summary>
public sealed class HostCompositionCharacterizationTests(TestcontainersTopologyFixture fixture)
{
    [Theory]
    [InlineData("User")]
    [InlineData("Administration")]
    [InlineData("Service")]
    [InlineData("v2-User")]
    [InlineData("v2-ProviderAdmin")]
    [InlineData("v2-Admin")]
    public async Task Should_ServeOpenApiDocument_WhenRequestedAnonymouslyThroughGateway(string documentName)
    {
        using var client = fixture.CreateAnonymousGatewayClient();

        using var response = await client.GetAsync(new Uri($"/api/{documentName}/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("\"openapi\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ExposePaginationAndETagHeaders_WhenRequestWithOriginHitsCorsPolicy()
    {
        using var client = fixture.CreateAnonymousGatewayClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        request.Headers.Add("Origin", "https://example-client.invalid");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"), "Expected the CORS policy to echo an Access-Control-Allow-Origin header.");

        var exposedHeaders = response.Headers.TryGetValues("Access-Control-Expose-Headers", out var values)
            ? string.Join(",", values)
            : string.Empty;

        Assert.Contains("Link", exposedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Total-Count", exposedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ETag", exposedHeaders, StringComparison.OrdinalIgnoreCase);
    }
}
