using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// B07-local comparison support shared by the four rest-of-read-surface parity classes
/// (ProjectTag / User / Public / Administration). Reuses the B03 <see cref="JsonShapeComparer"/>
/// engine and the B04→B03 <see cref="SpecToComparer"/> bridge, adding the two things the plan's
/// three-case <c>Divergence</c> allow-list does not model but these endpoints genuinely need:
/// <list type="bullet">
///   <item><b>Declared field drops.</b> Several v2 read DTOs intentionally slimmed the legacy
///     contract — dropping surrogate ids the natural key replaced (ADR-0022) or session/internal
///     ids the v2 identity contract no longer surfaces. A drop is an <em>intended</em> divergence,
///     so it is declared per-endpoint (with an ADR/redesign reason in the caller) and stripped from
///     the named side before the structural compare — never a silent tolerance.</item>
///   <item><b>Element-shape (tier-tolerant) list compare.</b> v2 list reads are caller-scoped only
///     (ADR-0018, 2026-06-16 amendment — the admin/provider tiers are deliberately deferred), so a
///     legacy admin-wide list and its v2 counterpart legitimately return <em>different row sets</em>.
///     Row-count/row-set equality is therefore not an invariant today; element <em>shape</em> is.
///     This compares the representative element of each side, and treats a v2 list that is empty
///     because the caller's scope is empty as the declared ADR-0018 tiering divergence.</item>
/// </list>
/// </summary>
internal static class B07Parity
{
    /// <summary>Maps a spec <see cref="Role"/> to the fixture's matching dev-auth client (mirrors B05).</summary>
    public static async Task<HttpClient> CreateRoleClientAsync(GatewayAttachFixture fixture, Role role, CancellationToken ct) => role switch
    {
        Role.Admin => fixture.CreateAdminGatewayClient(),
        Role.Owner => await fixture.CreateProjectOwnerClientAsync(ct),
        Role.Manager => await fixture.CreateProjectManagerClientAsync(ct),
        Role.Member => await fixture.CreateProjectMemberClientAsync(ct),
        Role.Anonymous => fixture.CreateAnonymousGatewayClient(),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported parity role."),
    };

    /// <summary>
    /// Structural compare of two object (or scalar) bodies after applying the declared divergence
    /// transforms and field drops. Property-name + JSON-kind sets must match; scalar values are
    /// ignored (GUIDs/timestamps differ legitimately).
    /// </summary>
    public static JsonShapeComparisonResult CompareObjects(
        string v1Body,
        string v2Body,
        IReadOnlyList<IShapeTransform> transforms,
        IReadOnlyCollection<string> v1Drop,
        IReadOnlyCollection<string> v2Drop)
    {
        var v1 = Normalize(v1Body, ComparisonSide.V1, transforms, v1Drop);
        var v2 = Normalize(v2Body, ComparisonSide.V2, transforms, v2Drop);

        using var v1Doc = JsonDocument.Parse(v1?.ToJsonString() ?? "null");
        using var v2Doc = JsonDocument.Parse(v2?.ToJsonString() ?? "null");

        return JsonShapeComparer.Compare(v1Doc.RootElement, v2Doc.RootElement);
    }

    /// <summary>
    /// Element-shape parity for two list bodies, tolerant of the ADR-0018 tiering row-set divergence.
    /// Returns a classified outcome: <see cref="ListShapeOutcome.ShapeMatched"/> when both sides had
    /// a representative element whose shapes matched; <see cref="ListShapeOutcome.V2ScopedEmpty"/>
    /// when v1 had rows but v2 was empty (the declared caller-scoping divergence — status parity still
    /// holds and is asserted separately); or a mismatch carrying the diff.
    /// </summary>
    public static ListShapeResult CompareListElementShape(
        string v1Body,
        string v2Body,
        IReadOnlyList<IShapeTransform> transforms,
        IReadOnlyCollection<string> v1Drop,
        IReadOnlyCollection<string> v2Drop)
    {
        var v1 = Normalize(v1Body, ComparisonSide.V1, transforms, v1Drop) as JsonArray;
        var v2 = Normalize(v2Body, ComparisonSide.V2, transforms, v2Drop) as JsonArray;

        if (v1 is null || v2 is null)
            return new ListShapeResult(ListShapeOutcome.NotAList, null);

        var v1First = v1.FirstOrDefault();
        var v2First = v2.FirstOrDefault();

        if (v2First is null)
        {
            // v2 empty: either both empty (nothing to compare) or v1 non-empty and v2 caller-scoped
            // to nothing — the declared ADR-0018 tiering divergence. Status parity is asserted elsewhere.
            return new ListShapeResult(
                v1First is null ? ListShapeOutcome.BothEmpty : ListShapeOutcome.V2ScopedEmpty,
                null);
        }

        if (v1First is null)
            return new ListShapeResult(ListShapeOutcome.V1Empty, null);

        using var v1Doc = JsonDocument.Parse(v1First.ToJsonString());
        using var v2Doc = JsonDocument.Parse(v2First.ToJsonString());
        var result = JsonShapeComparer.Compare(v1Doc.RootElement, v2Doc.RootElement);

        return new ListShapeResult(
            result.IsMatch ? ListShapeOutcome.ShapeMatched : ListShapeOutcome.ShapeMismatch,
            result);
    }

    /// <summary>
    /// Content-tolerant <em>contract</em>-shape compare: asserts both sides carry the same property
    /// <b>names</b> (recursively) and the same non-null JSON kinds, while tolerating the three ways v2
    /// reads legitimately differ in <em>content</em> from v1 without changing the contract —
    /// (a) a nullable field populated on one side and null on the other, (b) different list
    /// <em>lengths</em> (v2 caller-scoping, ADR-0018, returns a different row count), and
    /// (c) which representative element appears first. Used for the UserInfo surface, whose role list is
    /// scoped/sourced differently on v2 (ADR-0018 + Forge role service) yet must keep the same shape.
    /// Returns the list of contract (name/kind) divergences; empty means the contracts match.
    /// </summary>
    public static IReadOnlyList<string> CompareContractShape(
        string v1Body,
        string v2Body,
        IReadOnlyList<IShapeTransform> transforms,
        IReadOnlyCollection<string> v1Drop,
        IReadOnlyCollection<string> v2Drop)
    {
        var v1 = Normalize(v1Body, ComparisonSide.V1, transforms, v1Drop);
        var v2 = Normalize(v2Body, ComparisonSide.V2, transforms, v2Drop);

        var diffs = new List<string>();
        WalkContract(v1, v2, "$", diffs);
        return diffs;
    }

    private static void WalkContract(JsonNode? v1, JsonNode? v2, string path, List<string> diffs)
    {
        var k1 = v1?.GetValueKind() ?? JsonValueKind.Null;
        var k2 = v2?.GetValueKind() ?? JsonValueKind.Null;

        // A nullable slot null on one side is a value/content difference, not a contract difference.
        if (k1 == JsonValueKind.Null || k2 == JsonValueKind.Null)
            return;

        if (k1 != k2)
        {
            diffs.Add($"{path}: kind mismatch v1={k1} v2={k2}");
            return;
        }

        switch (v1)
        {
            case JsonObject o1 when v2 is JsonObject o2:
                var n1 = o1.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
                var n2 = o2.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var missing in n1.Except(n2).Order(StringComparer.Ordinal))
                    diffs.Add($"{path}.{missing}: property present on v1, absent on v2");
                foreach (var extra in n2.Except(n1).Order(StringComparer.Ordinal))
                    diffs.Add($"{path}.{extra}: property present on v2, absent on v1");
                foreach (var name in n1.Intersect(n2).Order(StringComparer.Ordinal))
                    WalkContract(o1[name], o2[name], $"{path}.{name}", diffs);
                break;

            case JsonArray a1 when v2 is JsonArray a2:
                // Length is content (caller-scoping); compare the representative element's shape only.
                if (a1.FirstOrDefault() is { } e1 && a2.FirstOrDefault() is { } e2)
                    WalkContract(e1, e2, $"{path}[*]", diffs);
                break;
        }
    }

    /// <summary>Element count of a JSON array body, or <see langword="null"/> when the body is not an array.</summary>
    public static int? ArrayCount(string body) =>
        JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body) is JsonArray array ? array.Count : null;

    private static JsonNode? Normalize(
        string body,
        ComparisonSide side,
        IReadOnlyList<IShapeTransform> transforms,
        IReadOnlyCollection<string> dropFields)
    {
        var node = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);

        foreach (var transform in transforms)
        {
            if (transform.Side == side || transform.Side == ComparisonSide.Both)
                node = transform.Apply(node);
        }

        return dropFields.Count == 0 ? node : Drop(node, dropFields);
    }

    private static JsonNode? Drop(JsonNode? node, IReadOnlyCollection<string> dropFields)
    {
        switch (node)
        {
            case JsonObject obj:
                var rebuilt = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (!dropFields.Contains(key))
                        rebuilt[key] = Drop(value, dropFields);
                }

                return rebuilt;

            case JsonArray array:
                var rebuiltArray = new JsonArray();
                foreach (var item in array)
                    rebuiltArray.Add(Drop(item, dropFields));

                return rebuiltArray;

            default:
                return node?.DeepClone();
        }
    }
}

/// <summary>Classified outcome of an element-shape list compare (see <see cref="B07Parity.CompareListElementShape"/>).</summary>
internal enum ListShapeOutcome
{
    /// <summary>Both sides had a representative element and their shapes matched.</summary>
    ShapeMatched,

    /// <summary>Representative element shapes diverged — carries the diff.</summary>
    ShapeMismatch,

    /// <summary>Both lists were empty; nothing to compare.</summary>
    BothEmpty,

    /// <summary>v1 had rows but v2 was empty — the declared ADR-0018 caller-scoping tiering divergence.</summary>
    V2ScopedEmpty,

    /// <summary>v2 had rows but v1 was empty (unexpected direction for a tiered read).</summary>
    V1Empty,

    /// <summary>One side did not deserialize to a JSON array.</summary>
    NotAList,
}

/// <summary>Result of <see cref="B07Parity.CompareListElementShape"/>.</summary>
internal sealed record ListShapeResult(ListShapeOutcome Outcome, JsonShapeComparisonResult? Comparison);
