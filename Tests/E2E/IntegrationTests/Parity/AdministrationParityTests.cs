using E2E.Fixtures;

using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// v1↔v2 <b>tier-semantics</b> parity for the eight legacy <c>Administration/*</c> GET reads
/// (ADR-0055 D4, ADR-0018). The legacy admin surface is admin-gated and returns data
/// <em>admin-wide</em>; v2 has no separate admin surface — the same resource's regular v2 read is
/// <b>caller-scoped only</b> because the admin/provider read-scope tiers are deliberately deferred
/// (ADR-0018, 2026-06-16 amendment). So this class does not assert row-set or admin-wide parity —
/// that would contradict the documented deferral. It asserts the invariants that <em>do</em> hold and
/// that the whole ADR exists to protect:
/// <list type="number">
///   <item><b>No server error on either stack</b> — the direct catch for the ORDER-BY-500 class and the
///     ADR-0018-amendment provider-stitch 500, across all eight admin reads and their v2 counterparts,
///     per role.</item>
///   <item><b>Only declared tier differences.</b> Every status on both sides is one of
///     {200, 401, 403, 404}: 401/403 is legacy admin gating, 404 is v2 scope-hiding
///     (ADR-0018 existence-hiding rider). Any other status — a 5xx especially — fails.</item>
///   <item><b>Tier direction.</b> Where both stacks return 200 for a list, the legacy admin-wide result
///     is a <em>superset</em> of the v2 caller-scoped result (v1 count ≥ v2 count). v2 returning
///     <em>more</em> than legacy admin-wide would be a scoping over-exposure and fails.</item>
/// </list>
/// Response <em>shape</em> parity for the underlying v2 reads (providers/locations/projects/users/
/// requests) is B05/B06's concern (they compare the same v2 endpoints against the v1 non-admin routes),
/// so it is intentionally not re-derived here.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class AdministrationParityTests(GatewayAttachFixture fixture)
{
    private const string SeedProviderName = "prov1";
    private const string SeedLocationName = "loc1";

    // The declared cross-stack status set: 200 OK, 401/403 legacy admin gating, 404 v2 scope-hiding.
    private static readonly HttpStatusCode[] DeclaredTierStatuses =
    [
        HttpStatusCode.OK,
        HttpStatusCode.Unauthorized,
        HttpStatusCode.Forbidden,
        HttpStatusCode.NotFound,
    ];

    public sealed record AdminCase(string Name, string V1Path, string V2Path, bool IsList);

    private static readonly IReadOnlyList<AdminCase> AllCases =
    [
        new("adm-project", "/api/Administration/Project", "/api/v2/Projects", true),
        new("adm-projecttag", "/api/Administration/ProjectTag", "/api/v2/ProjectTags", true),
        new("adm-user", "/api/Administration/User", "/api/v2/Users", true),
        new("adm-location", "/api/Administration/Location", "/api/v2/Locations", true),
        new("adm-provider", "/api/Administration/Provider", "/api/v2/Providers", true),
        new("adm-requests", "/api/Administration/Requests", "/api/v2/Requests", true),
        new("adm-location-byid", $"/api/Administration/Location/{SeedConstants.LocationId}", $"/api/v2/Providers/{SeedProviderName}/Locations/{SeedLocationName}", false),
        new("adm-provider-byid", $"/api/Administration/Provider/{SeedConstants.ProviderId}", $"/api/v2/Providers/{SeedProviderName}", false),
    ];

    public static IEnumerable<object[]> Cases =>
        from c in AllCases
        from role in new[] { Role.Admin, Role.Owner }
        select new object[] { c.Name, role };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Should_HoldTierSemantics_When_AdministrationReadThroughGateway(string caseName, Role role)
    {
        var adminCase = AllCases.Single(c => c.Name == caseName);
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, role, ct);

        using var v1 = await client.GetAsync(adminCase.V1Path, ct);
        using var v2 = await client.GetAsync(adminCase.V2Path, ct);

        var label = $"{adminCase.Name} [{role}]";

        // (1) No server error on either stack — the regression-class catch.
        Assert.True(
            (int)v1.StatusCode < 500,
            $"{label}: v1 {adminCase.V1Path} server-errored ({(int)v1.StatusCode}): {await v1.Content.ReadAsStringAsync(ct)}");
        Assert.True(
            (int)v2.StatusCode < 500,
            $"{label}: v2 {adminCase.V2Path} server-errored ({(int)v2.StatusCode}): {await v2.Content.ReadAsStringAsync(ct)}");

        // (2) Only declared tier differences (200 / 401 / 403 / 404) on both sides.
        Assert.True(
            DeclaredTierStatuses.Contains(v1.StatusCode),
            $"{label}: v1 status {(int)v1.StatusCode} is outside the declared tier set (200/401/403/404).");
        Assert.True(
            DeclaredTierStatuses.Contains(v2.StatusCode),
            $"{label}: v2 status {(int)v2.StatusCode} is outside the declared tier set (200/401/403/404).");

        // (3) Tier direction: legacy admin-wide is a superset of the v2 caller-scoped list.
        if (adminCase.IsList && v1.StatusCode == HttpStatusCode.OK && v2.StatusCode == HttpStatusCode.OK)
        {
            var v1Count = B07Parity.ArrayCount(await v1.Content.ReadAsStringAsync(ct));
            var v2Count = B07Parity.ArrayCount(await v2.Content.ReadAsStringAsync(ct));

            Assert.True(
                v1Count is { } admin && v2Count is { } scoped && admin >= scoped,
                $"{label}: tier direction violated — legacy admin-wide ({v1Count}) must be a superset of v2 caller-scoped ({v2Count}); v2 returning more is a scoping over-exposure (ADR-0018).");
        }
    }
}
