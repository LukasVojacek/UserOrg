using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests.AuthMatrix;

/// <summary>
/// Guards the matrix against silent coverage gaps: the routes the running Gateway serves live must be
/// exactly the routes <see cref="AuthMatrixTests"/> drives. A new v2 endpoint that ships without a
/// corresponding auth-matrix case (or a checked-in snapshot that drifts from the live surface) turns this
/// red — the "new endpoint without coverage → red" guarantee from plan-ready.md.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class AuthMatrixCompletenessTests(GatewayAttachFixture fixture)
{
    [Fact]
    public async Task Should_CoverEveryLiveRoute_When_MatrixDataSourceIsCompared()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateAnonymousGatewayClient();

        var liveRoutes = await RouteDiscovery.DiscoverLiveV2RoutesAsync(anonymous, ct);
        var matrixRoutes = AuthMatrixData.SnapshotRoutes();

        var uncovered = liveRoutes.Except(matrixRoutes).OrderBy(route => route.Path, StringComparer.Ordinal).ToList();
        var stale = matrixRoutes.Except(liveRoutes).OrderBy(route => route.Path, StringComparer.Ordinal).ToList();

        Assert.True(
            uncovered.Count == 0 && stale.Count == 0,
            $"Auth-matrix coverage drifted from the live surface.{Environment.NewLine}Live routes without a matrix case: {Describe(uncovered)}{Environment.NewLine}Matrix cases with no live route: {Describe(stale)}");
    }

    private static string Describe(List<Route> routes) =>
        routes.Count == 0 ? "(none)" : string.Join(", ", routes.Select(route => $"{route.HttpMethod} {route.Path}"));
}
