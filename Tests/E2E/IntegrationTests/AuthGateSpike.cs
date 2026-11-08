using System.Text.Json;

using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests;

/// <summary>
/// Front-loaded risk (handoff §7.1): a role-scoped authenticated v2 read through the Gateway must
/// succeed end-to-end over HTTP. This exercises the internal JWT with RequireHttpsMetadata=false
/// (Gateway → API), the RBAC seed (forge.Roles populated by the Gateway's hosted seed service), and the
/// DevAuthOverride identity — all against the Testcontainers-backed topology.
/// </summary>
/// <remarks>
/// Binds to the linked <see cref="ParityTestCollection"/> (from <c>ParityTests</c>, B02) rather than a
/// second, IntegrationTests-only collection: one <see cref="GatewayAttachFixture"/> instance per
/// assembly avoids double attach-mode seeding against the same live topology.
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class AuthGateSpike(GatewayAttachFixture fixture)
{
    private const string SeededProjectShortName = "TESTPRJ";

    [Fact]
    public async Task Should_ReturnOk_When_AuthorizedV2CallThroughGateway()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.GetAsync(new Uri($"/api/v2/Projects/{SeededProjectShortName}", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 for authorized v2 project read, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        // v2 responses serialize with PascalCase property names (see ParityTests keying on "ShortName").
        using var document = JsonDocument.Parse(body);
        Assert.Equal(SeededProjectShortName, document.RootElement.GetProperty("ShortName").GetString());
    }
}
