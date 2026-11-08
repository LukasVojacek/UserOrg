using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// Parity for the static / low-value read surface (ADR-0055 D4): the four Enums lists, the two
/// <c>Other</c> lookups (<c>Country</c>, <c>Statistics</c>) and <c>version</c>. Included for
/// completeness of the read map.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints are <b>structural-shape</b> parity only. Their <em>membership</em> legitimately
/// diverges between stacks — e.g. v1 <c>ERequestType</c> carries <c>Unknown</c> while v2 exposes
/// <c>Default</c> instead (<c>HEAppECreation</c> is present in both), and the two <c>LocationType</c>
/// enums come from different sources — so asserting v1==v2 element values, order, or list length
/// would flag intended cleanup, not a regression. Instead each array is reduced to a single
/// representative element (see <see cref="Skeleton"/>) and compared on its <em>item contract</em>
/// (property-name set + JSON kinds) via <see cref="JsonShapeComparer"/>. This is order- and
/// count-independent, matching the plan's "structural shape (not element order) is enough".
/// </para>
/// <para>
/// <c>version</c> is served as <c>text/plain</c> by v1 and as a JSON object (<c>{"Version": "..."}</c>)
/// by v2, both from the same process env (<c>APP_VERSION</c>), so it is asserted separately as a
/// status + value pair rather than through the structural-shape compare above.
/// </para>
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class StaticParityTests(GatewayAttachFixture fixture)
{
    // Confirmed v2 routes from src/Modules/Core/Composition/EndpointsExtensions.cs (api/v2 group):
    //   location-types / request-states / request-types / operation-modes / countries / statistics / version.
    // v1 routes: api/Enums/* , api/Other/{Country,Statistics} , api/version.
    private static readonly IReadOnlyList<ParitySpec> JsonSpecs =
    [
        new ParitySpec(
            "enums-location-types",
            "/api/Enums/LocationTypes",
            "/api/v2/LocationTypes",
            [Role.Admin],
            new Structural(),
            [new FieldRemoved("Id")]),
        new ParitySpec(
            "enums-request-states",
            "/api/Enums/RequestStates",
            "/api/v2/RequestStates",
            [Role.Admin],
            new Structural(),
            [new FieldRemoved("Id")]),
        new ParitySpec(
            "enums-request-types",
            "/api/Enums/RequestTypes",
            "/api/v2/RequestTypes",
            [Role.Admin],
            new Structural(),
            [new FieldRemoved("Id")]),
        new ParitySpec(
            "enums-operation-modes",
            "/api/Enums/OperationModeResponses",
            "/api/v2/OperationModes",
            [Role.Admin],
            new Structural(),
            [new FieldRemoved("Id")]),
        new ParitySpec(
            "country",
            "/api/Other/Country",
            "/api/v2/Countries",
            [Role.Admin],
            new Structural(),
            []),
        // Pin an explicit, wholly-in-the-past window so both stacks bucket the same weeks deterministically
        // (the default window depends on "today"); the skeleton compare then only needs a non-empty WeeklyData.
        new ParitySpec(
            "statistics",
            "/api/Other/Statistics?fromDate=2025-01-01&toDate=2025-01-31",
            "/api/v2/Statistics?fromDate=2025-01-01&toDate=2025-01-31",
            [Role.Admin],
            new Structural(),
            []),
    ];

    public static IEnumerable<object[]> JsonCases => JsonSpecs.Select(spec => new object[] { spec.Name });

    [Theory]
    [MemberData(nameof(JsonCases))]
    public async Task Should_MatchStatusAndItemStructure_When_StaticReadThroughGateway(string specName)
    {
        var spec = JsonSpecs.Single(candidate => candidate.Name == specName);
        var ct = TestContext.Current.CancellationToken;

        using var client = fixture.CreateAdminGatewayClient();

        using var v1 = await client.GetAsync(spec.V1Path, ct);
        using var v2 = await client.GetAsync(spec.V2Path, ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"{spec.Name}: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.");
        Assert.True(
            v1.StatusCode == HttpStatusCode.OK,
            $"{spec.Name}: v1 {spec.V1Path} returned {(int)v1.StatusCode}: {await v1.Content.ReadAsStringAsync(ct)}");

        var transforms = SpecToComparer.ToTransforms(spec.Known);

        using var v1Doc = JsonDocument.Parse(Skeleton(await v1.Content.ReadAsStringAsync(ct)));
        using var v2Doc = JsonDocument.Parse(Skeleton(await v2.Content.ReadAsStringAsync(ct)));

        var result = JsonShapeComparer.Compare(v1Doc.RootElement, v2Doc.RootElement, transforms: transforms);

        Assert.True(
            result.IsMatch,
            $"{spec.Name}: item-contract divergence:{Environment.NewLine}{result.ToDiffString()}");
    }

    [Fact]
    public async Task Should_MatchStatusAndVersionValue_When_VersionReadThroughGateway()
    {
        var ct = TestContext.Current.CancellationToken;

        using var client = fixture.CreateAnonymousGatewayClient();

        using var v1 = await client.GetAsync("/api/version", ct);
        using var v2 = await client.GetAsync("/api/v2/Version", ct);

        Assert.Equal(v1.StatusCode, v2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);

        var v1Body = (await v1.Content.ReadAsStringAsync(ct)).Trim();

        Assert.False(string.IsNullOrEmpty(v1Body), "v1 /api/version returned an empty body.");

        // v2 serializes with PropertyNamingPolicy = null (see HostInfrastructureExtensions), so the
        // JSON key is PascalCase "Version" — the lookup stays case-sensitive on purpose: a
        // case-insensitive one would still pass if the casing silently flipped, hiding the exact
        // regression this test exists to catch.
        var v2Raw = await v2.Content.ReadAsStringAsync(ct);
        using var v2Doc = ParseVersionJsonOrFail(v2Raw);

        Assert.True(
            v2Doc.RootElement.ValueKind == JsonValueKind.Object,
            $"v2 /api/v2/Version returned {v2Doc.RootElement.ValueKind}, expected a JSON object: {v2Raw}");

        Assert.True(
            v2Doc.RootElement.TryGetProperty("Version", out var v2VersionElement),
            $"v2 /api/v2/Version did not expose a PascalCase \"Version\" property: {v2Raw}");

        Assert.True(
            v2VersionElement.ValueKind == JsonValueKind.String,
            $"v2 /api/v2/Version serialized \"Version\" as {v2VersionElement.ValueKind}, expected a JSON string: {v2Raw}");

        Assert.Equal(v1Body, v2VersionElement.GetString()?.Trim());
    }

    // A v2 body-shape regression is exactly what this class is here to catch, and nobody can run the
    // class without Docker — so every shape failure has to arrive as a readable message carrying the
    // payload, never as a bare JsonException thrown from inside the parser.
    private static JsonDocument ParseVersionJsonOrFail(string rawBody)
    {
        try
        {
            return JsonDocument.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            Assert.Fail($"v2 /api/v2/Version did not return JSON ({ex.Message}): {rawBody}");
            throw;
        }
    }

    // Reduce every array to at most one representative element (recursively) so the structural compare
    // asserts the per-item contract rather than membership / count / order — correct for static enum and
    // lookup lists whose rows legitimately differ between the two stacks.
    private static string Skeleton(string json) =>
        Truncate(JsonNode.Parse(json))?.ToJsonString() ?? "null";

    private static JsonNode? Truncate(JsonNode? node) => node switch
    {
        JsonArray array => TruncateArray(array),
        JsonObject obj => TruncateObject(obj),
        _ => node?.DeepClone(),
    };

    private static JsonArray TruncateArray(JsonArray array)
    {
        var truncated = new JsonArray();
        if (array.Count > 0)
        {
            truncated.Add(Truncate(array[0]));
        }

        return truncated;
    }

    private static JsonObject TruncateObject(JsonObject obj)
    {
        var truncated = new JsonObject();
        foreach (var (key, value) in obj)
        {
            truncated[key] = Truncate(value);
        }

        return truncated;
    }
}
