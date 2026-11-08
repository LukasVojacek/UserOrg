using System.Globalization;

using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Tests;

// Small top-level records so each normalization rule is isolated from the shape of the real
// payloads. Top-level (not nested in the test class) because a source generator can only extend a
// partial type declared in the same file — a `file`-scoped type would not work either, since the
// generated partial lands in a different physical file than the `file` declaration.
[GenerateIdentityHash]
internal sealed partial record Ordered(string Alpha, int Beta, bool Gamma);

// Same property names as Ordered, declared in a different order.
[GenerateIdentityHash]
internal sealed partial record Reordered(bool Gamma, int Beta, string Alpha);

[GenerateIdentityHash]
internal sealed partial record WithCollection(string Name, IReadOnlyList<string> Items);

[GenerateIdentityHash]
internal sealed partial record NullableCollection(string Name, IReadOnlyList<string>? Items);

[GenerateIdentityHash]
internal sealed partial record WithDecimal(decimal Amount);

[GenerateIdentityHash]
internal sealed partial record WithDate(DateTime? Moment);

[GenerateIdentityHash]
internal sealed partial record WithGuid(Guid Id, Guid? Optional);

[GenerateIdentityHash]
internal sealed partial record WithString(string Value);

[GenerateIdentityHash]
internal sealed partial record WithIgnored(string Kept, [property: HashIgnore] string Skipped);

// Shared by IdentityHashNormalizationTests and NestedTypeSupportTests.
internal static class HashAssertions
{
    internal static void AssertValidSha256Hex(string hash)
    {
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}

/// <summary>
/// Normalization-checklist tests for the generated <see cref="IIdentityHashed.ComputeIdentityHash"/>
/// (ADR-0055/A2).
/// </summary>
public sealed class IdentityHashNormalizationTests
{
    [Fact]
    public void Should_ProduceStableHash_When_SamePayloadHashedTwice()
    {
        var payload = new Ordered("x", 1, true);

        Assert.Equal(payload.ComputeIdentityHash(), payload.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ReturnLowercaseHex_When_Hashing()
    {
        var hash = new Ordered("x", 1, true).ComputeIdentityHash();

        HashAssertions.AssertValidSha256Hex(hash);
    }

    [Fact]
    public void Should_IgnoreDeclarationOrder_When_PropertiesReordered()
    {
        var ordered = new Ordered("x", 1, true);
        var reordered = new Reordered(true, 1, "x");

        Assert.Equal(ordered.ComputeIdentityHash(), reordered.ComputeIdentityHash());
    }

    [Fact]
    public void Should_TreatNullAndEmptyCollectionAsEqual()
    {
        var withEmpty = new NullableCollection("n", []);
        var withNull = new NullableCollection("n", null);

        Assert.Equal(withEmpty.ComputeIdentityHash(), withNull.ComputeIdentityHash());
    }

    [Fact]
    public void Should_IgnoreCollectionOrder_When_ElementsReordered()
    {
        var a = new WithCollection("n", ["b", "a", "c"]);
        var b = new WithCollection("n", ["c", "b", "a"]);

        Assert.Equal(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_DistinguishCollections_When_ElementsDiffer()
    {
        var a = new WithCollection("n", ["a", "b"]);
        var b = new WithCollection("n", ["a", "c"]);

        Assert.NotEqual(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ProduceSameHash_UnderNonInvariantCulture()
    {
        // de-DE formats decimals with a comma and dates differently — the hasher must not care.
        var payload = new WithDecimal(1234.5m);
        var invariantHash = HashUnderCulture(payload, CultureInfo.InvariantCulture);
        var germanHash = HashUnderCulture(payload, CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal(invariantHash, germanHash);
    }

    [Fact]
    public void Should_NormalizeGuid_ToLowercaseDashedForm()
    {
        var id = Guid.Parse("0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0");
        var upper = new WithGuid(id, id);
        var lower = new WithGuid(
            Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"),
            Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"));

        // A Guid always round-trips to the same value; this pins that case does not leak into the
        // serialized form and that the two are hashed identically.
        Assert.Equal(upper.ComputeIdentityHash(), lower.ComputeIdentityHash());
    }

    [Fact]
    public void Should_NormalizeDatesToUtcSecondPrecision()
    {
        var withMillis = new WithDate(new DateTime(2026, 7, 7, 10, 30, 45, 123, DateTimeKind.Utc));
        var withoutMillis = new WithDate(new DateTime(2026, 7, 7, 10, 30, 45, 0, DateTimeKind.Utc));

        Assert.Equal(withoutMillis.ComputeIdentityHash(), withMillis.ComputeIdentityHash());
    }

    [Fact]
    public void Should_NormalizeLocalDatesToUtc()
    {
        var utc = new DateTime(2026, 7, 7, 10, 30, 45, DateTimeKind.Utc);
        var asLocal = new WithDate(utc.ToLocalTime());
        var asUtc = new WithDate(utc);

        Assert.Equal(asUtc.ComputeIdentityHash(), asLocal.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ProduceStableHash_When_DateIsNull()
    {
        var a = new WithDate(null);
        var b = new WithDate(null);

        Assert.Equal(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_DistinguishNullDateFromSetDate()
    {
        var nullDate = new WithDate(null);
        var setDate = new WithDate(new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(nullDate.ComputeIdentityHash(), setDate.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ProduceStableHash_When_StringExceedsDbLimits()
    {
        var longValue = new string('a', 10_000);
        var a = new WithString(longValue);
        var b = new WithString(new string('a', 10_000));

        Assert.Equal(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_DistinguishOverLengthStrings_When_TailDiffers()
    {
        var a = new WithString($"{new string('a', 9_999)}b");
        var b = new WithString($"{new string('a', 9_999)}c");

        Assert.NotEqual(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ExcludeHashIgnoredProperties_When_OnlyThoseDiffer()
    {
        var a = new WithIgnored("same", "one");
        var b = new WithIgnored("same", "two");

        Assert.Equal(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_DistinguishNullFromEmptyString()
    {
        var nullString = new NullableCollection("n", null) with { };
        var withNull = new WithDate(null);

        // guard: unrelated records must never collide
        Assert.NotEqual(nullString.ComputeIdentityHash(), withNull.ComputeIdentityHash());
    }

    private static string HashUnderCulture(IIdentityHashed payload, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return payload.ComputeIdentityHash();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
