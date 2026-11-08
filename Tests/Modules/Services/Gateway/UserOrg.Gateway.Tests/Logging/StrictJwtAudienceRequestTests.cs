using Microsoft.IdentityModel.JsonWebTokens;

namespace UserOrg.Gateway.Tests.Logging;

/// <summary>
/// A user request through the whole gateway pipeline with a token that carries an audience the gateway was not
/// configured for: refused with 422 before anything reaches the upstream while the strict check is on.
/// </summary>
public sealed class StrictJwtAudienceRequestTests
{
    [Fact]
    public async Task Should_Answer422_When_TheTokenCarriesAnUnconfiguredAudience()
    {
        await using var gateway = await GatewayLogHost.StartAsync();

        var status = await SendWithExtraAudienceAsync(gateway);

        Assert.Equal(422, status);
        Assert.Empty(gateway.UpstreamRequests);
    }

    [Fact]
    public async Task Should_NotAnswer422_When_StrictJwtAudienceIsOff()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(StrictJwtAudience: "false"));

        var status = await SendWithExtraAudienceAsync(gateway);

        Assert.NotEqual(422, status);
    }

    private static Task<int> SendWithExtraAudienceAsync(GatewayLogHost gateway)
    {
        var token = GatewayLogHost.MintIdpToken(
            new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = "strict-audience-user",
                [JwtRegisteredClaimNames.Jti] = "strict-audience-jti",
            },
            [GatewayLogHost.Audience, "other"]);

        return gateway.SendAsync(RouteSurfaceFixture.ServedV2Path, new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
    }
}
