using E2E.Fixtures;

namespace ParityTests;

/// <summary>
/// Trivial boot smoke test: proves the <c>ParityTests</c> assembly can attach to the shared
/// topology via <see cref="GatewayAttachFixture"/> and reach the Gateway before any real
/// parity spec work (B03+) lands.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class ParitySmokeTests(GatewayAttachFixture fixture)
{
    [Fact]
    public async Task Should_ReturnOk_WhenGetVersionCalledThroughGateway()
    {
        using var response = await fixture.GatewayClient.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
