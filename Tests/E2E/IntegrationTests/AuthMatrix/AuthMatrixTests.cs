using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests.AuthMatrix;

/// <summary>
/// Data-driven authorization matrix: every discovered v2 route × every role tier (84 × 5 = 420 cases),
/// plus the unserved paths of <see cref="AuthMatrixData.UnservedPaths"/> × every tier,
/// each asserting the live Gateway's authorization outcome against B01's pure
/// <see cref="RbacExpectationBuilder"/> prediction. Requests are issued through the per-role Gateway
/// clients the topology fixture provides; only the authorization outcome is asserted (never a 2xx body or
/// a persisted side effect — see <see cref="AuthMatrixData.BuildRequest"/> for the mutation-safety rules).
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class AuthMatrixTests(GatewayAttachFixture fixture)
{
    public static TheoryData<string, string, string, ExpectedOutcome> Cases => AuthMatrixData.BuildCases();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Should_MatchGatewayAuthorizationOutcome_When_RouteIsCalledForRole(
        string path,
        string httpMethod,
        string role,
        ExpectedOutcome expected)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await CreateClientForRoleAsync(role, ct);
        using var request = AuthMatrixData.BuildRequest(path, httpMethod);

        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct);

        // Restore the seed before asserting: an Allowed-tier DELETE project soft-deletes the shared
        // seed project, and healing must happen whether or not the assertion below passes so it never
        // leaks into the next case.
        if (AuthMatrixData.IsSeedProjectDelete(path, httpMethod) && status is >= 200 and < 300)
        {
            await fixture.ResetToSeedAsync(ct);
        }

        var context = $"{httpMethod} {path} as {role}: expected {expected}, got {status}.{Environment.NewLine}{body}";
        switch (expected)
        {
            case ExpectedOutcome.Allowed:
                // Excludes 5xx too: a broken handler or unmocked dependency must not pass as authorized.
                Assert.True(status is >= 200 and < 500 and not 401 and not 403, context);
                // A plain "not 404" would be wrong here: BuildRequest aims most Allowed-tier writes at an
                // absent child on purpose, so their 404 comes from upstream. Only the gateway's own
                // route-existence 404 (ADR-0106) means the oracle denied a route that does exist.
                Assert.False(GatewayRouteNotFound.Matches(status, body), context);
                break;
            case ExpectedOutcome.Forbidden403:
                Assert.True(status == 403, context);
                break;
            case ExpectedOutcome.Unauthorized401:
                Assert.True(status == 401, context);
                break;
            case ExpectedOutcome.NotFound404:
                Assert.True(GatewayRouteNotFound.Matches(status, body), context);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expected), expected, "Unknown expected outcome.");
        }
    }

    private Task<HttpClient> CreateClientForRoleAsync(string role, CancellationToken ct) => role switch
    {
        "Admin" => Task.FromResult(fixture.CreateAdminGatewayClient()),
        "Owner" => fixture.CreateProjectOwnerClientAsync(ct),
        "Manager" => fixture.CreateProjectManagerClientAsync(ct),
        "Member" => fixture.CreateProjectMemberClientAsync(ct),
        "Anonymous" => Task.FromResult(fixture.CreateAnonymousGatewayClient()),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown auth-matrix role.")
    };
}
