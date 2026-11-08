using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// B06 (ADR-0055 D3/D4, ADR-0018): live v1↔v2 read parity for the by-identifier reads of
/// ProjectResource, Provider and Location, across the surrogate-GUID → natural-key divergence and the
/// F0.3 read-scope tiering.
/// </summary>
/// <remarks>
/// <para>
/// <b>Route-shape divergence (declared, allow-listed).</b> Legacy addresses these by surrogate GUID
/// (<c>/api/Provider/{id}</c>, <c>/api/Location/{id}</c>, <c>/api/ProjectResource/{id}</c>); v2 addresses
/// them by their natural key nested under the provider (<c>/api/v2/Providers/{name}</c>,
/// <c>…/locations/{name}</c>, <c>…/resources/{name}</c>) per ADR-0022/ADR-0029. This is a spec-level path
/// substitution, not a body divergence — the same seeded row is fetched both ways. As in
/// <see cref="ProjectParityTests"/>, the reshaped DTOs are broader than the declared-divergence allow-list
/// can express, so the proven cross-stack contract is <b>(1) HTTP status parity</b> and <b>(2) natural-key
/// identity parity</b> (both responses expose the same <c>Name</c> — proving the GUID and the natural key
/// resolve the <em>same</em> entity), reusing <see cref="SpecToComparer"/> + <see cref="JsonShapeComparer"/>.
/// </para>
/// <para>
/// <b>F0.3 by-id tiering (ADR-0018).</b> Each pair runs under <see cref="Role.Admin"/> and the scoped
/// <see cref="Role.Owner"/>. v2's by-id handlers branch on <c>caller.ResolveReadScope</c>: admin/provider
/// resolve any active row; a scoped user resolves the row only when reachable through one of their projects
/// (<c>TESTPRJ</c> reaches <c>prov1</c> / <c>loc1</c> / <c>res1</c> via its resource assignment on
/// <c>loc1</c>). Both tiers therefore resolve the seeded rows, and the test asserts both tiers see the same
/// entity on both stacks — the tier semantics hold.
/// </para>
/// <para>
/// <b>Wrong-provider → 404 parity (ADR-0018 existence-hiding rider).</b> v2
/// <c>/api/v2/Providers/prov2/Resources/res1</c> asks for a real resource under the <em>wrong</em> provider
/// and must 404 — byte-indistinguishable from genuinely-missing, so existence never leaks. The legacy analog
/// of an unreachable resource is a GUID that resolves to nothing: v1 <c>/api/ProjectResource/{unknown}</c>
/// also 404s (<c>RecordNotFoundException</c>). Both stacks answer an unreachable/mis-addressed resource with
/// 404 (never 200-with-null, never 500) — the parity this asserts.
/// </para>
/// <para>
/// <b>MANDATORY INVESTIGATION — the Location list-set divergence (B05 surfaced, classified here).</b>
/// B05 observed v1 <c>GET /api/Location</c> and v2 <c>GET /api/v2/Locations</c> returning different row
/// <em>sets</em> for the same identity. Root cause, confirmed from source:
/// <list type="bullet">
///   <item>v1 <c>/api/Location</c> (<c>LocationEndpoint</c>) calls
///     <c>ILocationService.GetRelatedLocationAsync(sub, providerId, …)</c> — it is <b>always caller-scoped</b>
///     to the user's own projects/provider and has <b>no admin bypass</b>. The legacy admin-wide location
///     read is a <em>different</em> route, <c>GET /api/Administration/Location</c>
///     (<c>AdministrationEndpoint</c>, which calls <c>GetLocationAsync(id, onlyPublic:false)</c> / the
///     unscoped list).</item>
///   <item>v2 <c>/api/v2/Locations</c> (<c>GetLocationsEndpoint</c>) <b>unifies both</b> legacy routes into
///     one, tiered by <c>caller.ResolveReadScope</c> (ADR-0018 F0.3): <c>Admin</c> → <c>ReadScope.All</c>
///     (every active location — folding in the legacy <c>Administration/Location</c> admin-wide read);
///     a scoped user → only project-reachable locations.</item>
/// </list>
/// <b>Verdict: INTENDED F0.3 divergence, NOT a regression, and NOT a private-data leak.</b> The extra rows
/// v2 returns to an <em>admin</em> are exactly the admin-wide set legacy exposed via
/// <c>Administration/Location</c>; v1 <c>/api/Location</c> simply never had an admin tier. For a
/// <em>non-admin</em> (scoped) caller the two stacks return the identical project-reachable set — v2 does not
/// widen a regular user's view, so no private/unreachable location leaks. The honest parity mapping is
/// therefore <c>Administration/Location</c> ↔ <c>v2/locations</c>@Admin and <c>/api/Location</c> ↔
/// <c>v2/locations</c>@scoped; comparing <c>/api/Location</c> to <c>v2/locations</c> under Admin is an
/// apples-to-oranges tier mismatch, not a bug. No clarification is raised. The scoped-tier equality is what
/// this suite asserts (via the Owner-tier by-id reads below and B05's scoped list parity); the admin-tier
/// widening is the same one classified for the project list in <see cref="ProjectParityTests"/>.
/// </para>
/// <para>
/// <b>RESOLVED FINDING — location by-id was unreachable through the Gateway.</b> F7 originally caught that
/// v2 <c>GET /api/v2/Providers/{pn}/Locations/{name}</c> returned <b>403 "no permission mapping"</b> while
/// v1 <c>GET /api/Location/{id}</c> returned 200: the Gateway RBAC config mapped a stale flat
/// <c>GET /api/v2/Locations/{id}</c> (a route removed by the ADR-0022 nesting) and lacked a GET mapping for
/// the nested route. Fixed by ADR-0059 (#242): the nested GET is now mapped and the stale flat entry
/// removed, so all three by-id reads below assert strict parity with no known-divergence remaps.
/// </para>
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class ResourceProviderLocationByIdParityTests(GatewayAttachFixture fixture)
{
    private const string SeededProviderName = "prov1";
    private const string SeededLocationName = "loc1";
    private const string SeededResourceName = "res1";
    private const string WrongProviderName = "prov2";

    // v1 GUID route ↔ v2 natural-key route. Each seeded row is fetched both ways; Name is the natural key
    // whose value must match across stacks, proving the GUID and the natural key resolve the same entity.
    private static readonly IReadOnlyList<ByIdSpec> AllSpecs =
    [
        new ByIdSpec(
            "provider-by-id",
            $"/api/Provider/{SeedConstants.ProviderId}",
            $"/api/v2/Providers/{SeededProviderName}",
            [Role.Admin, Role.Owner],
            "Name",
            SeededProviderName,
            []),
        new ByIdSpec(
            "location-by-id",
            $"/api/Location/{SeedConstants.LocationId}",
            $"/api/v2/Providers/{SeededProviderName}/Locations/{SeededLocationName}",
            [Role.Admin, Role.Owner],
            "Name",
            SeededLocationName,
            []),
        new ByIdSpec(
            "resource-by-id",
            $"/api/ProjectResource/{SeedConstants.ProjectResourceId}",
            $"/api/v2/Providers/{SeededProviderName}/Resources/{SeededResourceName}",
            [Role.Admin, Role.Owner],
            "Name",
            SeededResourceName,
            []),
    ];

    private sealed record ByIdSpec(string Name, string V1Path, string V2Path, Role[] Roles, string KeyField, string ExpectedKey, Divergence[] Known);

    public static IEnumerable<object[]> SpecCases => AllSpecs.Select(spec => new object[] { spec.Name });

    [Theory]
    [MemberData(nameof(SpecCases))]
    public async Task Should_MatchStatusAndNaturalKey_When_ByIdReadThroughGatewayAcrossTiers(string specName)
    {
        var spec = AllSpecs.Single(candidate => candidate.Name == specName);
        var ct = TestContext.Current.CancellationToken;

        foreach (var role in spec.Roles)
        {
            using var client = await fixture.CreateClientAsync(role, ct);

            using var v1 = await client.GetAsync(spec.V1Path, ct);
            using var v2 = await client.GetAsync(spec.V2Path, ct);

            var v1Body = await v1.Content.ReadAsStringAsync(ct);
            var v2Body = await v2.Content.ReadAsStringAsync(ct);

            // (1) Status parity across the surrogate-GUID → natural-key divergence, per tier — unless a
            // StatusRemap divergence declares an intended (or known-defect) status difference for this pair.
            if (SpecToComparer.FindStatusRemap(spec.Known) is { } remap)
            {
                Assert.Equal(remap.V1, v1.StatusCode);
                Assert.Equal(remap.V2, v2.StatusCode);
            }
            else
            {
                Assert.True(
                    v1.StatusCode == v2.StatusCode,
                    $"{spec.Name} [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.{Environment.NewLine}v1: {v1Body}{Environment.NewLine}v2: {v2Body}");
            }

            // The natural-key identity compare below is only meaningful when v2 actually returned the entity.
            if (v1.StatusCode != HttpStatusCode.OK || v2.StatusCode != HttpStatusCode.OK)
                continue;

            // (2) Natural-key identity parity — the GUID (v1) and the natural key (v2) resolve the same row.
            var v1Key = SingleKey(v1Body, spec.KeyField);
            var v2Key = SingleKey(v2Body, spec.KeyField);

            Assert.True(
                string.Equals(v1Key, spec.ExpectedKey, StringComparison.OrdinalIgnoreCase),
                $"{spec.Name} [{role}]: v1 {spec.V1Path} resolved '{spec.KeyField}'='{v1Key}', expected '{spec.ExpectedKey}'.{Environment.NewLine}{v1Body}");
            Assert.True(
                string.Equals(v2Key, spec.ExpectedKey, StringComparison.OrdinalIgnoreCase),
                $"{spec.Name} [{role}]: v2 {spec.V2Path} resolved '{spec.KeyField}'='{v2Key}', expected '{spec.ExpectedKey}'.{Environment.NewLine}{v2Body}");

            // Reuse the structural comparer to lock the must-match key across the two single-object responses.
            using var v1Doc = JsonDocument.Parse(new JsonObject { [spec.KeyField] = v1Key }.ToJsonString());
            using var v2Doc = JsonDocument.Parse(new JsonObject { [spec.KeyField] = v2Key }.ToJsonString());
            var result = JsonShapeComparer.Compare(v1Doc.RootElement, v2Doc.RootElement, [spec.KeyField]);
            Assert.True(result.IsMatch, $"{spec.Name} [{role}] natural-key mismatch:{Environment.NewLine}{result.ToDiffString()}");
        }
    }

    /// <summary>
    /// ADR-0018 existence-hiding rider: an unreachable / mis-addressed resource returns 404 on both stacks —
    /// v2 for a real resource under the wrong provider, v1 for a GUID that resolves to nothing. Neither leaks
    /// existence (no 200-with-null) nor 500s.
    /// </summary>
    [Fact]
    public async Task Should_Return404OnBothStacks_When_ResourceUnreachableByWrongProviderOrUnknownId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();

        // v2: res1 exists but is not hosted by prov2 → 404, indistinguishable from genuinely-missing.
        using var v2WrongProvider = await client.GetAsync(
            $"/api/v2/Providers/{WrongProviderName}/Resources/{SeededResourceName}", ct);
        Assert.Equal(HttpStatusCode.NotFound, v2WrongProvider.StatusCode);

        // v1 legacy analog of an unreachable resource: a GUID that resolves to nothing → 404.
        using var v1Unknown = await client.GetAsync($"/api/ProjectResource/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, v1Unknown.StatusCode);
    }

    private static string? SingleKey(string body, string keyField)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        var node = JsonNode.Parse(body);
        var obj = node switch
        {
            JsonObject o => o,
            JsonArray { Count: > 0 } a => a[0] as JsonObject,
            _ => null,
        };
        return obj is not null && obj.TryGetPropertyValue(keyField, out var value) && value is JsonValue v && v.TryGetValue(out string? s)
            ? s
            : null;
    }
}
