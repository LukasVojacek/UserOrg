using System.Text.Json;

namespace ParityTests.Comparison;

/// <summary>
/// Pure in-process unit tests for <see cref="JsonShapeComparer"/>. No topology, no SQL,
/// no HTTP — these run in milliseconds against <see cref="JsonDocument"/> fixtures and
/// are deliberately kept out of the gateway-attach collection so they never touch the
/// live topology.
/// </summary>
public sealed class JsonShapeComparerTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Should_Match_When_StructurallyIdentical()
    {
        var v1 = Parse("""{ "name": "a", "count": 1, "active": true }""");
        var v2 = Parse("""{ "name": "a", "count": 1, "active": true }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_Match_When_ScalarValuesDiffer_ButStructureSame()
    {
        // GUIDs / timestamps differ legitimately between stacks — structure is what matters.
        var v1 = Parse("""{ "id": "11111111-1111-1111-1111-111111111111", "createdAt": "2024-01-01", "count": 1 }""");
        var v2 = Parse("""{ "id": "22222222-2222-2222-2222-222222222222", "createdAt": "2026-07-08", "count": 99 }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_Flag_When_PropertySetDiffers()
    {
        var v1 = Parse("""{ "name": "a", "email": "x@y" }""");
        var v2 = Parse("""{ "name": "a", "phone": "123" }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        Assert.False(result.IsMatch);
        Assert.Contains(result.Mismatches, m => m.Reason.Contains("email"));
        Assert.Contains(result.Mismatches, m => m.Reason.Contains("phone"));
    }

    [Fact]
    public void Should_Flag_When_KindDiffers()
    {
        var v1 = Parse("""{ "count": 1 }""");
        var v2 = Parse("""{ "count": "1" }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        Assert.False(result.IsMatch);
        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal("$.count", mismatch.Path);
        Assert.Contains("Number", mismatch.Reason);
        Assert.Contains("String", mismatch.Reason);
    }

    [Fact]
    public void Should_Flag_When_ArrayLengthDiffers()
    {
        var v1 = Parse("""{ "items": [1, 2, 3] }""");
        var v2 = Parse("""{ "items": [1, 2] }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        Assert.False(result.IsMatch);
        Assert.Contains(result.Mismatches, m => m.Path == "$.items" && m.Reason.Contains("length"));
    }

    [Fact]
    public void Should_Flag_When_ArrayOrderDiffers_AndOrderingKeyIsMustMatch()
    {
        // Same elements, same structure, different order — only a must-match on the
        // ordering key catches it. This is the 2026-06-16 ORDER BY regression axis.
        var v1 = Parse("""[ { "name": "alpha" }, { "name": "beta" } ]""");
        var v2 = Parse("""[ { "name": "beta" }, { "name": "alpha" } ]""");

        var structural = JsonShapeComparer.Compare(v1, v2);
        Assert.True(structural.IsMatch, "structure alone must not catch order");

        var ordered = JsonShapeComparer.Compare(v1, v2, mustMatchFields: ["name"]);
        Assert.False(ordered.IsMatch);
        Assert.Contains(ordered.Mismatches, m => m.Path == "$[0].name");
    }

    [Fact]
    public void Should_Match_When_MustMatchFieldValuesEqual()
    {
        var v1 = Parse("""[ { "name": "alpha", "id": "a" }, { "name": "beta", "id": "b" } ]""");
        var v2 = Parse("""[ { "name": "alpha", "id": "z" }, { "name": "beta", "id": "y" } ]""");

        // "name" must match (ordering key); "id" may differ.
        var result = JsonShapeComparer.Compare(v1, v2, mustMatchFields: ["name"]);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_Flag_When_MustMatchFieldValueDiffers()
    {
        var v1 = Parse("""{ "name": "alpha" }""");
        var v2 = Parse("""{ "name": "beta" }""");

        var result = JsonShapeComparer.Compare(v1, v2, mustMatchFields: ["name"]);

        Assert.False(result.IsMatch);
        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal("$.name", mismatch.Path);
        Assert.Contains("alpha", mismatch.Reason);
        Assert.Contains("beta", mismatch.Reason);
    }

    [Fact]
    public void Should_Match_When_FieldRenameTransformApplied()
    {
        // v1 calls it "shortName", v2 renamed it "code" — declared divergence.
        var v1 = Parse("""{ "shortName": "TESTPRJ", "count": 1 }""");
        var v2 = Parse("""{ "code": "TESTPRJ", "count": 1 }""");

        var withoutTransform = JsonShapeComparer.Compare(v1, v2);
        Assert.False(withoutTransform.IsMatch);

        var transforms = new IShapeTransform[]
        {
            new FieldRenameTransform("shortName", "code", ComparisonSide.V1),
        };
        var result = JsonShapeComparer.Compare(v1, v2, mustMatchFields: ["code"], transforms: transforms);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_Match_When_ValueObjectEnvelopeUnwrapped()
    {
        // v1 renders a bare string; v2 wraps the typed id in { "Value": ... }.
        var v1 = Parse("""{ "shortName": "TESTPRJ" }""");
        var v2 = Parse("""{ "shortName": { "Value": "TESTPRJ" } }""");

        var withoutTransform = JsonShapeComparer.Compare(v1, v2);
        Assert.False(withoutTransform.IsMatch);

        var transforms = new IShapeTransform[]
        {
            new ValueObjectEnvelopeTransform("shortName", ComparisonSide.V2),
        };
        var result = JsonShapeComparer.Compare(v1, v2, mustMatchFields: ["shortName"], transforms: transforms);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_UnwrapRootEnvelope_When_PathUnspecified()
    {
        var v1 = Parse(""" "TESTPRJ" """);
        var v2 = Parse("""{ "Value": "TESTPRJ" }""");

        var transforms = new IShapeTransform[]
        {
            new ValueObjectEnvelopeTransform(PropertyName: null, ComparisonSide.V2),
        };
        var result = JsonShapeComparer.Compare(v1, v2, transforms: transforms);

        Assert.True(result.IsMatch, result.ToDiffString());
    }

    [Fact]
    public void Should_ProduceReadableDiff_When_Mismatched()
    {
        var v1 = Parse("""{ "user": { "name": "a" } }""");
        var v2 = Parse("""{ "user": { "name": 1 } }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        Assert.False(result.IsMatch);
        var diff = result.ToDiffString();
        Assert.Contains("$.user.name", diff);
    }

    [Fact]
    public void Should_Throw_When_ThrowIfMismatchCalledOnMismatch()
    {
        var v1 = Parse("""{ "a": 1 }""");
        var v2 = Parse("""{ "b": 1 }""");

        var result = JsonShapeComparer.Compare(v1, v2);

        var ex = Assert.Throws<JsonShapeMismatchException>(result.ThrowIfMismatch);
        Assert.Contains("$", ex.Message, StringComparison.Ordinal);
    }
}
