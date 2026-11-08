using System.Text.Json;
using System.Text.Json.Nodes;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// Pure in-process unit tests for the <see cref="SpecToComparer"/> bridge that adapts the
/// declarative B04 <see cref="Divergence"/> allow-list to the B03 <see cref="IShapeTransform"/>
/// engine. No topology, no SQL — kept out of the gateway-attach collection so it never touches
/// the live topology. B06/B07/B08 reuse this bridge, so its contract is pinned here.
/// </summary>
public sealed class SpecToComparerTests
{
    [Fact]
    public void Should_MapFieldRenamed_ToV1SideFieldRenameTransform()
    {
        var transforms = SpecToComparer.ToTransforms([new FieldRenamed("shortName", "name")]);

        var transform = Assert.IsType<FieldRenameTransform>(Assert.Single(transforms));
        Assert.Equal("shortName", transform.From);
        Assert.Equal("name", transform.To);
        // The rename is applied on v1 so it adopts v2's naming before the structural compare.
        Assert.Equal(ComparisonSide.V1, transform.Side);
    }

    [Fact]
    public void Should_MapFieldRemoved_ToV1SideFieldRemoveTransform()
    {
        var transforms = SpecToComparer.ToTransforms([new FieldRemoved("Id")]);

        var transform = Assert.IsType<FieldRemoveTransform>(Assert.Single(transforms));
        Assert.Equal("Id", transform.Name);
        Assert.Equal(ComparisonSide.V1, transform.Side);
    }

    [Fact]
    public void Should_MatchV2ItemWithoutId_When_V1IdDeclaredRemoved()
    {
        var v1 = JsonNode.Parse("""[ { "Id": 1, "Name": "HPC" } ]""");
        var v2 = JsonNode.Parse("""[ { "Name": "HPC" } ]""");

        var transforms = SpecToComparer.ToTransforms([new FieldRemoved("Id")]);

        using var undeclaredV1 = JsonDocument.Parse(v1!.ToJsonString());
        using var docV1 = JsonDocument.Parse(ApplyForSide(v1, transforms, ComparisonSide.V1)!.ToJsonString());
        using var docV2 = JsonDocument.Parse(ApplyForSide(v2, transforms, ComparisonSide.V2)!.ToJsonString());

        Assert.False(JsonShapeComparer.Compare(undeclaredV1.RootElement, docV2.RootElement).IsMatch);
        Assert.True(JsonShapeComparer.Compare(docV1.RootElement, docV2.RootElement).IsMatch);
    }

    [Fact]
    public void Should_MapFieldKindChanged_ToV1SideFieldKindChangeTransform()
    {
        var transforms = SpecToComparer.ToTransforms([new FieldKindChanged("RequestState")]);

        var transform = Assert.IsType<FieldKindChangeTransform>(Assert.Single(transforms));
        Assert.Equal("RequestState", transform.Name);
        Assert.Equal(ComparisonSide.V1, transform.Side);
    }

    [Fact]
    public void Should_MatchV2Name_When_V1NumberDeclaredKindChanged()
    {
        var v1 = JsonNode.Parse("""{ "RequestState": 1, "Name": "x" }""");
        var v2 = JsonNode.Parse("""{ "RequestState": "Pending", "Name": "x" }""");

        var result = CompareDeclared(v1, v2, [new FieldKindChanged("RequestState")]);
        using var undeclaredV1 = JsonDocument.Parse(v1!.ToJsonString());
        using var undeclaredV2 = JsonDocument.Parse(v2!.ToJsonString());

        Assert.False(JsonShapeComparer.Compare(undeclaredV1.RootElement, undeclaredV2.RootElement).IsMatch);
        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_StillFlagV2Number_When_KindChangeDeclared()
    {
        var v1 = JsonNode.Parse("""{ "RequestState": 1 }""");
        var v2 = JsonNode.Parse("""{ "RequestState": 1 }""");

        var result = CompareDeclared(v1, v2, [new FieldKindChanged("RequestState")]);

        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal("$.RequestState", mismatch.Path);
        Assert.Contains("kind mismatch", mismatch.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_StillFlagMissingV2Property_When_KindChangeDeclared()
    {
        var v1 = JsonNode.Parse("""{ "RequestState": 1, "Name": "x" }""");
        var v2 = JsonNode.Parse("""{ "Name": "x" }""");

        var result = CompareDeclared(v1, v2, [new FieldKindChanged("RequestState")]);

        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal("$.RequestState", mismatch.Path);
        Assert.Contains("missing property", mismatch.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_MapValueObjectEnvelope_ToV1SideEnvelopeTransform()
    {
        var transforms = SpecToComparer.ToTransforms([new ValueObjectEnvelope("ProjectShortName")]);

        var transform = Assert.IsType<ValueObjectEnvelopeTransform>(Assert.Single(transforms));
        Assert.Equal("ProjectShortName", transform.PropertyName);
        // v1 renders the value object enveloped ({ "Value": x }); unwrap it on v1 to match bare v2.
        Assert.Equal(ComparisonSide.V1, transform.Side);
    }

    [Fact]
    public void Should_SkipStatusRemap_WhenBuildingShapeTransforms()
    {
        // StatusRemap is asserted at the HTTP-status-compare step, not as a body-shape transform.
        var transforms = SpecToComparer.ToTransforms(
            [new StatusRemap(HttpStatusCode.OK, HttpStatusCode.NotFound), new FieldRenamed("a", "b")]);

        Assert.Single(transforms);
        Assert.All(transforms, t => Assert.IsType<FieldRenameTransform>(t));
    }

    [Fact]
    public void Should_ReturnDeclaredStatusRemap_WhenPresent()
    {
        var remap = SpecToComparer.FindStatusRemap(
            [new FieldRenamed("a", "b"), new StatusRemap(HttpStatusCode.OK, HttpStatusCode.NotFound)]);

        Assert.NotNull(remap);
        Assert.Equal(HttpStatusCode.OK, remap.V1);
        Assert.Equal(HttpStatusCode.NotFound, remap.V2);
    }

    [Fact]
    public void Should_ReturnNullStatusRemap_WhenNoneDeclared()
    {
        Assert.Null(SpecToComparer.FindStatusRemap([new FieldRenamed("a", "b")]));
    }

    [Fact]
    public void Should_UnwrapV1Envelope_SoOrderingKeyMatchesBareV2()
    {
        // End-to-end of the bridge: an enveloped v1 ordering key ({ "Value": "TESTPRJ" }) is unwrapped
        // to a bare string, so the must-match ordering-key compare against bare v2 succeeds.
        var v1 = JsonNode.Parse("""[ { "ProjectShortName": { "Value": "TESTPRJ" } } ]""");
        var v2 = JsonNode.Parse("""[ { "ProjectShortName": "TESTPRJ" } ]""");

        var transforms = SpecToComparer.ToTransforms([new ValueObjectEnvelope("ProjectShortName")]);

        var normalizedV1 = ApplyForSide(v1, transforms, ComparisonSide.V1);
        var normalizedV2 = ApplyForSide(v2, transforms, ComparisonSide.V2);

        using var docV1 = JsonDocument.Parse(normalizedV1!.ToJsonString());
        using var docV2 = JsonDocument.Parse(normalizedV2!.ToJsonString());

        var result = JsonShapeComparer.Compare(docV1.RootElement, docV2.RootElement, ["ProjectShortName"]);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    private static JsonShapeComparisonResult CompareDeclared(JsonNode? v1, JsonNode? v2, Divergence[] known)
    {
        var transforms = SpecToComparer.ToTransforms(known);
        using var docV1 = JsonDocument.Parse(ApplyForSide(v1, transforms, ComparisonSide.V1)!.ToJsonString());
        using var docV2 = JsonDocument.Parse(ApplyForSide(v2, transforms, ComparisonSide.V2)!.ToJsonString());
        return JsonShapeComparer.Compare(docV1.RootElement, docV2.RootElement);
    }

    private static JsonNode? ApplyForSide(JsonNode? node, IReadOnlyList<IShapeTransform> transforms, ComparisonSide side)
    {
        foreach (var transform in transforms)
        {
            if (transform.Side == side || transform.Side == ComparisonSide.Both)
            {
                node = transform.Apply(node);
            }
        }

        return node;
    }
}
