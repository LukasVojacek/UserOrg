using System.IdentityModel.Tokens.Jwt;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Tests.Logging;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The whole gateway pipeline with <c>FeatureManagement:V1Api</c> off: a v1 path is answered 404 by the
/// route-existence stage and never reaches the upstream, while the ungated legacy endpoints keep their
/// own answers.
/// </summary>
/// <remarks>
/// The legacy route space is built from the Service document, which is always loaded and describes the
/// ungated <c>/api/version</c> and <c>/api/DataStaging</c>. Moving either behind the V1 gate would turn
/// them into 404s and fail these tests. The 404 is deliberately not decoupled from the Service document:
/// a surface that describes nothing must stay fail-safe (see <c>RouteSurface.cs</c> lines 26-30 and
/// 70-73), and answering 404 for it would reverse that.
/// </remarks>
public sealed class GatewayV1OffRouteNotFoundTests
{
    [Theory]
    [InlineData("/api/Users")]
    [InlineData("/api/UserInfo")]
    [InlineData("/api/Administration/User")]
    public async Task Should_Answer404RouteNotFound_When_V1ApiIsOffAndTheRouteIsAV1Route(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        await using var gateway = await StartV1OffGatewayAsync();
        var bearer = new Dictionary<string, string> { ["Authorization"] = $"Bearer {MintToken()}" };

        var anonymous = await gateway.SendAsync(path);
        var authenticated = await gateway.SendAsync(path, bearer);

        Assert.Equal(StatusCodes.Status404NotFound, anonymous);
        Assert.Equal(StatusCodes.Status404NotFound, authenticated);
        Assert.Empty(gateway.UpstreamRequests);
    }

    [Theory]
    [InlineData("/api/version", StatusCodes.Status200OK)]
    [InlineData("/api/remi/users", StatusCodes.Status401Unauthorized)]
    [InlineData("/api/DataStaging/WorkerData", StatusCodes.Status401Unauthorized)]
    public async Task Should_NotAnswer404_When_V1ApiIsOffAndTheRouteIsUngated(string path, int expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(path);
        await using var gateway = await StartV1OffGatewayAsync();

        var status = await gateway.SendAsync(path);

        Assert.Equal(expectedStatus, status);
    }

    [Fact]
    public async Task Should_KeepTodaysAnswer_When_TheServiceDocumentDescribesNothing()
    {
        await using var gateway = await GatewayLogHost.StartAsync(
            new GatewayLogOptions(V1ApiEnabled: false, RouteSurface: RouteSurfaceFixture.V2OnlySurface()));

        var status = await gateway.SendAsync("/api/Users");

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    private static Task<GatewayLogHost> StartV1OffGatewayAsync() =>
        GatewayLogHost.StartAsync(new GatewayLogOptions(V1ApiEnabled: false, RouteSurface: RouteSurfaceFixture.PublishedV1OffSurface()));

    private static string MintToken() =>
        GatewayLogHost.MintIdpToken(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = "v1-off-user",
            [JwtRegisteredClaimNames.Jti] = "v1-off-token-jti",
        });
}
