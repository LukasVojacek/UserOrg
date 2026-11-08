using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// THE regression-proof block (ADR-0055 — the whole reason this ADR exists).
/// </summary>
/// <remarks>
/// <para>
/// On 2026-06-16 a manual Gateway round-trip found the five list endpoints below returning
/// <b>HTTP 500</b> from an untranslatable <c>ORDER BY &lt;valueObject&gt;.Value</c> in their paged
/// SQL. EF in-memory translated <c>.Value</c> fine and stayed green; only real SQL surfaced the 500.
/// This suite is the automated proof of that class of bug: for every v1↔v2 pair it asserts, per role,
/// through the Gateway against real SQL,
/// </para>
/// <list type="number">
///   <item><b>Status parity</b> — both stacks return 200. This is the direct cross-stack catch for the
///     ORDER-BY 500: a v2 500 against a v1 200 fails immediately.</item>
///   <item><b>v2 ordering correctness</b> — v2's list is actually sorted by the field its query's
///     <c>ORDER BY</c> sorts on (<see cref="ShapeRule"/> = <see cref="MustMatchFields"/>). Implemented by
///     comparing v2's response, index-by-index, against the same rows re-sorted by that key
///     (<see cref="JsonShapeComparer"/> with the key as a must-match field): if v2 dropped or broke its
///     ORDER BY, actual ≠ sorted and the must-match compare fails. With the B01 seed augmentation
///     (≥2 rows per list) this is observable.</item>
/// </list>
/// <para>
/// <b>Why the ordering axis is proven on v2 alone, not by v1==v2 order equality.</b> Live data shows
/// the two stacks legitimately order (and scope) these lists differently: v1 <c>/api/Provider</c> orders
/// by insertion/id and lists <c>prov1, prov2, EFP-Core</c>, whereas v2 orders alphabetically by name and
/// lists <c>EFP-Core, prov1, prov2</c>; v1 <c>/api/Location</c> and v2 <c>/api/v2/Locations</c> even
/// return different row sets (a list-scoping divergence that is B06/B07's concern, not this block's). So
/// v1-order == v2-order is not a valid invariant. The regression this ADR exists to catch is the
/// <em>500</em> (asserted cross-stack via status parity) on the <em>ORDER BY</em> axis (asserted by v2
/// being correctly ordered). Both together would have failed on the original regression and fail again if
/// it returns. Whole-body cross-stack shape parity is deferred to B06/B07.
/// </para>
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class RegressedListEndpointsParityTests(GatewayAttachFixture fixture)
{
    // The seeded provider whose nested resource route the v2 resource list lives under (ADR-0022).
    private const string SeededProviderName = "prov1";

    // Exactly the five endpoints that returned HTTP 500 on 2026-06-16. Ordering keys are the field each
    // v2 query's ORDER BY sorts on, expressed on the response shape:
    //   providers  → Provider.Name          (ProviderDetailsQuery: OrderBy p.Name)
    //   locations  → Location.Name           (LocationDetailsQuery: orderby loc.Name)
    //   resources  → ProjectResource.Name    (ResourceDetailsQuery: orderby pr.ResourceName)
    //   requests   → Request.ProjectId, exposed as ProjectShortName (RequestDetailsQuery: OrderBy r.ProjectId)
    // For the two request lists the ORDER BY is on ProjectId (tie-broken on the unexposed request Id) and
    // every seeded request lives on one project (TESTPRJ), so the exposed ordering key is constant: the
    // status-parity check still catches the 500, but a same-project reshuffle can't be observed until
    // multi-project request seeding lands (Phase-2 seed work) — see the class remarks.
    private static readonly IReadOnlyList<ParitySpec> AllSpecs =
    [
        new ParitySpec(
            "resources",
            "/api/ProjectResource?ProjectShortName=TESTPRJ",
            $"/api/v2/Providers/{SeededProviderName}/Resources",
            [Role.Admin, Role.Owner],
            new MustMatchFields("Name"),
            []),
        new ParitySpec(
            "providers",
            "/api/Provider",
            "/api/v2/Providers",
            [Role.Admin, Role.Owner],
            new MustMatchFields("Name"),
            []),
        new ParitySpec(
            "locations",
            "/api/Location",
            "/api/v2/Locations",
            [Role.Admin, Role.Owner],
            new MustMatchFields("Name"),
            []),
        new ParitySpec(
            "requests",
            "/api/Requests",
            "/api/v2/Requests",
            [Role.Admin, Role.Owner],
            new MustMatchFields("ProjectShortName"),
            [new ValueObjectEnvelope("ProjectShortName")]),
        new ParitySpec(
            "requests-sent",
            "/api/Requests/SentRequests",
            "/api/v2/Requests/Sent",
            [Role.Admin, Role.Owner],
            new MustMatchFields("ProjectShortName"),
            [new ValueObjectEnvelope("ProjectShortName")]),
    ];

    public static IEnumerable<object[]> SpecCases => AllSpecs.Select(spec => new object[] { spec.Name });

    /// <summary>
    /// 2026-06-16 <c>ORDER BY &lt;VO&gt;.Value</c> HTTP-500 regression proof: each regressed list endpoint
    /// returns 200 on both stacks (status parity — the direct catch for the 500) and v2 is correctly
    /// ordered on its ordering key (a dropped/broken ORDER BY in v2 fails the must-match compare).
    /// </summary>
    [Theory]
    [MemberData(nameof(SpecCases))]
    public async Task Should_MatchStatusAndOrdering_When_RegressedListReadThroughGateway(string specName)
    {
        var spec = AllSpecs.Single(candidate => candidate.Name == specName);
        var ct = TestContext.Current.CancellationToken;

        foreach (var role in spec.Roles)
        {
            using var client = await CreateRoleClientAsync(role, ct);

            using var v1 = await client.GetAsync(spec.V1Path, ct);
            using var v2 = await client.GetAsync(spec.V2Path, ct);

            // (1) Status parity — the direct cross-stack catch for the ORDER-BY 500 (v2 500 vs v1 200).
            AssertStatusParity(spec, role, v1.StatusCode, v2.StatusCode);

            // Both must be 200 for the ordering compare to mean anything (and 200 is the regression proof).
            Assert.True(
                v1.StatusCode == HttpStatusCode.OK,
                $"{spec.Name} [{role}]: v1 {spec.V1Path} returned {(int)v1.StatusCode}: {await v1.Content.ReadAsStringAsync(ct)}");
            Assert.True(
                v2.StatusCode == HttpStatusCode.OK,
                $"{spec.Name} [{role}]: v2 {spec.V2Path} returned {(int)v2.StatusCode}: {await v2.Content.ReadAsStringAsync(ct)}");

            // (2) Ordering proof — v2 is actually sorted by the field its ORDER BY sorts on. Compare v2 as
            //     returned against the same rows re-sorted by the ordering key: equal ⇒ v2 was ordered.
            var orderingKeys = spec.Shape is MustMatchFields must ? must.Paths : [];
            var transforms = SpecToComparer.ToTransforms(spec.Known);

            var v2Keyed = NormaliseToKeys(await v2.Content.ReadAsStringAsync(ct), orderingKeys, transforms, ComparisonSide.V2);
            var v2Sorted = SortByKeys(v2Keyed, orderingKeys);

            using var actualDoc = JsonDocument.Parse(v2Keyed.ToJsonString());
            using var sortedDoc = JsonDocument.Parse(v2Sorted.ToJsonString());

            var result = JsonShapeComparer.Compare(actualDoc.RootElement, sortedDoc.RootElement, orderingKeys);

            Assert.True(
                result.IsMatch,
                $"{spec.Name} [{role}] v2 is not ordered by [{string.Join(", ", orderingKeys)}] — a dropped/broken ORDER BY:{Environment.NewLine}{result.ToDiffString()}");
        }
    }

    private static void AssertStatusParity(ParitySpec spec, Role role, HttpStatusCode v1Status, HttpStatusCode v2Status)
    {
        if (SpecToComparer.FindStatusRemap(spec.Known) is { } remap)
        {
            Assert.Equal(remap.V1, v1Status);
            Assert.Equal(remap.V2, v2Status);
            return;
        }

        Assert.True(
            v1Status == v2Status,
            $"{spec.Name} [{role}]: status divergence v1={(int)v1Status} v2={(int)v2Status} (the ORDER-BY 500 class).");
    }

    // Reduce a list to a bare array of { orderingKey: value } after applying the side's declared
    // divergence transforms (envelope unwrap, field rename). Reducing to the ordering key keeps the
    // structural compare apples-to-apples for the must-match ordering assertion.
    private static JsonArray NormaliseToKeys(
        string body,
        IReadOnlyList<string> keys,
        IReadOnlyList<IShapeTransform> transforms,
        ComparisonSide side)
    {
        JsonNode? node = JsonNode.Parse(body);
        foreach (var transform in transforms)
        {
            if (transform.Side == side || transform.Side == ComparisonSide.Both)
            {
                node = transform.Apply(node);
            }
        }

        var normalised = new JsonArray();
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var projected = new JsonObject();
                foreach (var key in keys)
                {
                    projected[key] = item is JsonObject obj && obj.TryGetPropertyValue(key, out var value)
                        ? value?.DeepClone()
                        : null;
                }

                normalised.Add(projected);
            }
        }

        return normalised;
    }

    // A stable sort by the ordering-key values (as strings, case-insensitive to mirror the SQL default
    // collation). Stable so equal keys — e.g. same-project requests — keep v2's order, making the compare
    // a genuine "was v2 already sorted?" check rather than an imposed reordering.
    private static JsonArray SortByKeys(JsonArray source, IReadOnlyList<string> keys)
    {
        var ordered = source
            .OfType<JsonObject>()
            .OrderBy(obj => KeyTuple(obj, keys), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sorted = new JsonArray();
        foreach (var obj in ordered)
        {
            sorted.Add(obj.DeepClone());
        }

        return sorted;
    }

    private static string KeyTuple(JsonObject obj, IReadOnlyList<string> keys) =>
        string.Join(
            "|",
            keys.Select(key => obj.TryGetPropertyValue(key, out var value) && value is not null ? value.ToJsonString() : " "));

    private async Task<HttpClient> CreateRoleClientAsync(Role role, CancellationToken ct) => role switch
    {
        Role.Admin => fixture.CreateAdminGatewayClient(),
        Role.Owner => await fixture.CreateProjectOwnerClientAsync(ct),
        Role.Manager => await fixture.CreateProjectManagerClientAsync(ct),
        Role.Member => await fixture.CreateProjectMemberClientAsync(ct),
        Role.Anonymous => fixture.CreateAnonymousGatewayClient(),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported parity role."),
    };
}
