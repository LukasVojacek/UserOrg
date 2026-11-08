using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// B06 (ADR-0055 D3/D4): live v1↔v2 read parity for the rest of the Project read surface —
/// the project list plus its by-short-name sub-resources (users, valid-roles, templates, extended).
/// </summary>
/// <remarks>
/// <para>
/// <b>What is asserted (and why not whole-body equality).</b> The v2 DDD refactor deliberately
/// reshaped these DTOs (surrogate GUID <c>Id</c> dropped, <c>OperationMode</c> → <c>Mode</c>, nested
/// objects collapsed to natural-key strings, new fields added). That reshape is broader than the
/// declared-divergence allow-list (ADR-0055 D4: <see cref="FieldRenamed"/> / <see cref="ValueObjectEnvelope"/>
/// / <see cref="StatusRemap"/>) can express, and inventing a per-field "removed/collapsed" divergence
/// for every DTO would be exactly the ad-hoc normalizer ADR-0055 rejected. So the cross-stack contract
/// proven here is the one that carries the regression risk and the user-visible guarantee:
/// </para>
/// <list type="number">
///   <item><b>HTTP status parity</b> — both stacks return the same status (the 500-vs-200 class the whole
///     ADR exists to catch, and the ADR-0018 existence-hiding 404 contract).</item>
///   <item><b>Natural-key identity parity</b> — both responses expose the seeded natural-key value
///     (<c>ShortName</c> / <c>IdpUserId</c> / role <c>Name</c>), and v2 never surfaces a key outside v1's
///     set (<c>v2 ⊆ v1</c> — no over-exposure). For the pairs whose two stacks scope to the identical row
///     set (<see cref="IdenticalScopeSpecs"/>: users, valid-roles, extended) this tightens to full key-set
///     equality via <see cref="SpecToComparer"/> + <see cref="JsonShapeComparer"/>: each side is reduced to
///     a key-only projection, both are sorted by the key (neutralising the known v1-orders-by-id /
///     v2-orders-alphabetically divergence — see B05 remarks), then compared with the key as a
///     <see cref="MustMatchFields"/> value.</item>
/// </list>
/// <para>
/// <b>Role choice = the scoped (Owner) tier, and two intended scoping divergences.</b> Every pair runs
/// under <see cref="Role.Owner"/>. Two intended divergences make strict list set-equality invalid and are
/// deliberately tolerated (asserting only seeded-key presence + <c>v2 ⊆ v1</c> for the project list):
/// <list type="bullet">
///   <item><b>Created-by vs membership (ADR-0006).</b> Legacy <c>GET /api/Project</c> returns projects the
///     caller <em>created or is a member of</em>, so for the Owner it includes <c>NOROLE</c> (owner is its
///     <c>CreatedByUserId</c> but holds no role on it — see the seed remark "creator != membership"). v2
///     <c>GET /api/v2/Projects</c> scopes strictly by project-role membership, so it correctly omits
///     <c>NOROLE</c>. v2 is the intended behaviour; v1 ⊋ v2.</item>
///   <item><b>Admin-tier widening (ADR-0018 F0.3).</b> Under <see cref="Role.Admin"/> v2 gives the sees-all
///     tier while legacy <c>/api/Project</c> has no admin bypass — the same widening classified for
///     locations in <see cref="ResourceProviderLocationByIdParityTests"/>. The list therefore runs under
///     Owner only, not Admin.</item>
/// </list>
/// </para>
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class ProjectParityTests(GatewayAttachFixture fixture)
{
    private const string SeededProjectShortName = "TESTPRJ";

    // Each pair: the v1↔v2 routes, the roles to run under, and the natural key whose value-set must match
    // across stacks (Structural = status-parity only, used for the HEAppE-backed templates pair whose deeply
    // nested shape has no single natural key to key on).
    private static readonly IReadOnlyList<ParitySpec> AllSpecs =
    [
        // Project list: Owner tier only — v1 and v2 both scope to the caller's projects here, so the
        // key-set is a real invariant. (Admin tier diverges by F0.3 design; see class remarks.)
        new ParitySpec(
            "project-list",
            "/api/Project",
            "/api/v2/Projects",
            [Role.Owner],
            new MustMatchFields("ShortName"),
            []),
        new ParitySpec(
            "project-users",
            $"/api/Project/{SeededProjectShortName}/Users",
            $"/api/v2/Projects/{SeededProjectShortName}/Users",
            [Role.Owner],
            new MustMatchFields("IdpUserId"),
            []),
        new ParitySpec(
            "project-valid-roles",
            $"/api/Project/{SeededProjectShortName}/ValidRoles",
            $"/api/v2/Projects/{SeededProjectShortName}/ValidRoles",
            [Role.Owner],
            new MustMatchFields("Name"),
            []),
        // v1 ExtendedProjectInfo returns a single-element list; v2 …/extended returns a single object.
        // The key extractor handles both root kinds, so the ShortName key-set is comparable either way.
        new ParitySpec(
            "project-extended",
            $"/api/Project/ExtendedProjectInfo?ProjectShortName={SeededProjectShortName}",
            $"/api/v2/Projects/{SeededProjectShortName}/Extended",
            [Role.Owner],
            new MustMatchFields("ShortName"),
            []),
        // NOTE — TemplatesInfo/{sn} ↔ projects/{sn}/templates is deliberately NOT asserted here. It is
        // HEAppE-backed (WireMock, ADR-0051), i.e. the external-service / S2S dimension the plan explicitly
        // DEFERS. It also carries a genuine behavioural divergence: when a cluster's HEAppE instance is
        // unreachable, legacy fails hard (HTTP 503 "HEAppE service unavailable") while v2 degrades gracefully
        // (HTTP 200 with per-cluster { "IsAvailable": false, "Nodes": [] }). That is an intended v2
        // improvement, not a regression, but it makes cross-stack status parity meaningless and couples the
        // result to external-service health — so it belongs with the deferred DDI/Service read-parity work,
        // not this core reads suite.
    ];

    // Specs whose two stacks scope to the identical row set, so a strict key-set equality is a real invariant.
    // (The project list is intentionally excluded — see the class remarks: legacy also surfaces created-by
    // projects, so v1 ⊋ v2 there by ADR-0006 design.)
    private static readonly HashSet<string> IdenticalScopeSpecs = ["project-users", "project-valid-roles", "project-extended"];

    // The seeded natural-key value each pair's list/object must contain, so a both-sides-empty result can
    // never silently pass the equality compare.
    private static readonly Dictionary<string, string> ExpectedSeededKey = new()
    {
        ["project-list"] = SeededProjectShortName,
        // The Owner's dev-auth idpid (GatewayAttachFixture OwnerIdpId), surfaced as ProjectUsers.IdpUserId.
        ["project-users"] = "e2e-owner-idp",
        ["project-valid-roles"] = "Member",
        ["project-extended"] = SeededProjectShortName,
    };

    public static IEnumerable<object[]> SpecCases => AllSpecs.Select(spec => new object[] { spec.Name });

    [Theory]
    [MemberData(nameof(SpecCases))]
    public async Task Should_MatchStatusAndNaturalKey_When_ProjectReadThroughGateway(string specName)
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

            // (1) Status parity — the direct catch for the ORDER-BY-500 class and the 404 existence contract.
            Assert.True(
                v1.StatusCode == v2.StatusCode,
                $"{spec.Name} [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.{Environment.NewLine}v1: {v1Body}{Environment.NewLine}v2: {v2Body}");

            if (v1.StatusCode != HttpStatusCode.OK || spec.Shape is not MustMatchFields must)
                continue;

            // (2) Natural-key identity parity.
            var key = must.Paths[0];
            var transforms = SpecToComparer.ToTransforms(spec.Known);

            var v1Keys = KeyProjection(v1Body, key, transforms, ComparisonSide.V1);
            var v2Keys = KeyProjection(v2Body, key, transforms, ComparisonSide.V2);

            // The seeded, role-scoped entity the Owner IS entitled to must be visible on BOTH stacks — the
            // core cross-stack read guarantee, and a guard so a both-sides-empty result can never pass.
            if (ExpectedSeededKey.TryGetValue(spec.Name, out var seeded))
            {
                Assert.True(
                    ContainsKey(v1Keys, key, seeded),
                    $"{spec.Name} [{role}]: v1 {spec.V1Path} did not expose the seeded '{key}'='{seeded}'.{Environment.NewLine}{v1Body}");
                Assert.True(
                    ContainsKey(v2Keys, key, seeded),
                    $"{spec.Name} [{role}]: v2 {spec.V2Path} did not expose the seeded '{key}'='{seeded}'.{Environment.NewLine}{v2Body}");
            }

            if (IdenticalScopeSpecs.Contains(spec.Name))
            {
                // Same-scope pairs: full key-set equality via the structural comparer (sorted projections).
                using var v1Doc = JsonDocument.Parse(v1Keys.ToJsonString());
                using var v2Doc = JsonDocument.Parse(v2Keys.ToJsonString());
                var result = JsonShapeComparer.Compare(v1Doc.RootElement, v2Doc.RootElement, [key]);
                Assert.True(
                    result.IsMatch,
                    $"{spec.Name} [{role}] natural-key set diverged on '{key}':{Environment.NewLine}{result.ToDiffString()}{Environment.NewLine}v1: {v1Body}{Environment.NewLine}v2: {v2Body}");
            }
            else
            {
                // The project list: v2's role-scoped set must not surface a project outside v1's
                // create-or-member set (no over-exposure). v1 ⊋ v2 (created-by projects) is tolerated.
                var v1Set = KeySet(v1Keys, key);
                var v2Set = KeySet(v2Keys, key);
                Assert.True(
                    v2Set.IsSubsetOf(v1Set),
                    $"{spec.Name} [{role}]: v2 exposed keys outside v1's set [{string.Join(", ", v2Set.Except(v1Set))}].{Environment.NewLine}v1: {v1Body}{Environment.NewLine}v2: {v2Body}");
            }
        }
    }

    private static HashSet<string> KeySet(JsonArray projection, string key) =>
        projection.OfType<JsonObject>()
            .Select(obj => obj.TryGetPropertyValue(key, out var value) && value is JsonValue v && v.TryGetValue(out string? s) ? s : null)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Reduce a response to a sorted bare array of { key: value }, tolerating either a top-level array (lists)
    // or a single object (by-id / single-object reads). Declared divergences are applied first so an
    // enveloped or renamed key still lines up. Sorting by the key neutralises the known cross-stack ordering
    // divergence (v1 by id, v2 alphabetical) so the compare is a set compare, not an order compare.
    private static JsonArray KeyProjection(
        string body,
        string key,
        IReadOnlyList<IShapeTransform> transforms,
        ComparisonSide side)
    {
        JsonNode? node = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
        foreach (var transform in transforms)
        {
            if (transform.Side == side || transform.Side == ComparisonSide.Both)
                node = transform.Apply(node);
        }

        var items = node switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject obj => [obj],
            _ => Enumerable.Empty<JsonObject>(),
        };

        var projected = items
            .Select(obj => obj.TryGetPropertyValue(key, out var value) && value is not null ? value.ToJsonString() : "null")
            .OrderBy(raw => raw, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new JsonArray();
        foreach (var raw in projected)
            result.Add(new JsonObject { [key] = JsonNode.Parse(raw) });

        return result;
    }

    private static bool ContainsKey(JsonArray projection, string key, string expected) =>
        projection.OfType<JsonObject>()
            .Any(obj => obj.TryGetPropertyValue(key, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue(out string? s)
                && string.Equals(s, expected, StringComparison.OrdinalIgnoreCase));
}
