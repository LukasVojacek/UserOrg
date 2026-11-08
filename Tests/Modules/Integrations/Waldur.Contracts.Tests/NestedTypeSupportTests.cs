using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Tests;

// Mirrors the CQRS request/response-nested-in-a-static-endpoint-class shape used elsewhere in this
// codebase (e.g. GetProjectTemplatesEndpoint) — records nested inside a static class, one of them
// holding a collection of another nested record.
public static partial class NestedContainer
{
    [GenerateIdentityHash]
    public sealed partial record Outer(string Name, IReadOnlyList<Inner> Items);

    [GenerateIdentityHash]
    public sealed partial record Inner(string Value);
}

public static partial class DeepContainer
{
    public partial class Middle
    {
        [GenerateIdentityHash]
        public sealed partial record Leaf(string Value);
    }
}

public sealed class NestedTypeSupportTests
{
    [Fact]
    public void Should_ReturnLowercaseHex_When_HashingNestedType()
    {
        var payload = new NestedContainer.Outer("n", [new NestedContainer.Inner("a")]);

        var hash = payload.ComputeIdentityHash();

        HashAssertions.AssertValidSha256Hex(hash);
    }

    [Fact]
    public void Should_IgnoreCollectionOrder_When_ElementsAreNestedType()
    {
        var a = new NestedContainer.Outer("n", [new NestedContainer.Inner("a"), new NestedContainer.Inner("b")]);
        var b = new NestedContainer.Outer("n", [new NestedContainer.Inner("b"), new NestedContainer.Inner("a")]);

        Assert.Equal(a.ComputeIdentityHash(), b.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ComputeHash_When_TypeIsNestedTwoLevelsDeep()
    {
        var hash = new DeepContainer.Middle.Leaf("x").ComputeIdentityHash();

        HashAssertions.AssertValidSha256Hex(hash);
    }
}
