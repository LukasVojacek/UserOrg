using Core.DataAccess.Queries;

namespace Core.UnitTests.Queries;

public sealed class GroupingTests
{
    private sealed record Row(int Key, string Value);

    [Fact]
    public void Should_GroupElementsByKey_When_KeysRepeat()
    {
        Row[] rows = [new(1, "a"), new(2, "b"), new(1, "c")];

        var result = rows.GroupToLists(r => r.Key, r => r.Value);

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "a", "c" }, result[1]);
        Assert.Equal(new[] { "b" }, result[2]);
    }

    [Fact]
    public void Should_ReturnEmptyDictionary_When_SourceEmpty()
    {
        var result = Array.Empty<Row>().GroupToLists(r => r.Key, r => r.Value);

        Assert.Empty(result);
    }

    [Fact]
    public void Should_ProjectEachElement_When_Grouping()
    {
        Row[] rows = [new(1, "x"), new(1, "y")];

        var result = rows.GroupToLists(r => r.Key, r => r.Value.ToUpperInvariant());

        Assert.Equal(new[] { "X", "Y" }, result[1]);
    }
}
