using E2E.Fixtures;

using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// v1↔v2 read parity for the anonymous public request-info surface (ADR-0055 D4/D5):
/// <c>GET /api/Public/RequestInfo/{requestId}</c> ↔
/// <c>GET /api/v2/Public/Requests/ByLink/{actionLink}</c>.
/// </summary>
/// <remarks>
/// Both routes are anonymous (no bearer). They address the <em>same</em> seeded request by different
/// keys — the declared addressing divergence (a path substitution, not a body shape): v1 by the
/// request's surrogate GUID, v2 by the single-use action-link token (ADR-0024). Declared body
/// divergences: v1 also surfaces the surrogate <c>RequestId</c> the v2 token-addressed contract drops,
/// and v1 renders <c>ProjectShortName</c> as an enveloped value object (<c>{ "Value": … }</c>) where
/// v2 renders it bare, and v1 renders <c>RequestState</c> and <c>RequestType</c> as numbers where v2 renders
/// their names. The seed request (44444444-…-444444444444) is pending, type 2, with valid
/// action links, so both stacks return 200.
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class PublicParityTests(GatewayAttachFixture fixture)
{
    // The seed request's accept action-link token (seed-data.sql). v2 addresses the request by it.
    private const string SeedActionLink = "accept-seed-request";

    // v1 PublicRequestModel also carries the surrogate RequestId; the v2 token-addressed contract
    // does not surface it (ADR-0024). Declared drop, not a silent tolerance.
    private static readonly string[] V1OnlyFields = ["RequestId"];

    // SUSPECTED BUG — deferred pending decision, see .ai/clarifications/B07.md. The v2 endpoint is
    // .AllowAnonymous() at the Core level, but the Gateway's PublicEndpointsOptions anonymous allow-list
    // contains "/api/Public" (prefix) and NOT "/api/v2/Public", so the anonymous v2 route is blocked at
    // the gateway with 401 while v1 returns 200. Reads-only parity cannot fix gateway config; skipping
    // (not asserting a false "intended" divergence) so the gap is surfaced, not silently passed. Once the
    // gateway allow-list adds "/api/v2/Public", un-skip: both stacks then return 200 and the body-shape
    // assertion below (RequestId drop + ProjectShortName envelope unwrap) proves the shape parity.
    [Fact]
    public async Task Should_MatchStatusAndShape_When_AnonymousReadsPublicRequestInfo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, Role.Anonymous, ct);

        using var v1 = await client.GetAsync($"/api/Public/RequestInfo/{SeedConstants.SeedRequestId}", ct);
        using var v2 = await client.GetAsync($"/api/v2/Public/Requests/ByLink/{SeedActionLink}", ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"public-request-info [Anonymous]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode} — v1 body: {await v1.Content.ReadAsStringAsync(ct)}; v2 body: {await v2.Content.ReadAsStringAsync(ct)}");

        Assert.Equal(HttpStatusCode.OK, v2.StatusCode);

        // v1 renders ProjectShortName enveloped; unwrap it on v1 to match bare v2 (precedent: B05 requests).
        var transforms = SpecToComparer.ToTransforms(
            [new ValueObjectEnvelope("ProjectShortName"), new FieldKindChanged("RequestState"), new FieldKindChanged("RequestType")]);

        var result = B07Parity.CompareObjects(
            await v1.Content.ReadAsStringAsync(ct),
            await v2.Content.ReadAsStringAsync(ct),
            transforms,
            v1Drop: V1OnlyFields,
            v2Drop: []);

        Assert.True(result.IsMatch, $"public-request-info shape diverged:{Environment.NewLine}{result.ToDiffString()}");
    }
}
