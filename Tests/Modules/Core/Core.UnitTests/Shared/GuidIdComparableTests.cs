using Core.Domain.Request;

namespace Core.UnitTests.Shared;

/// <summary>
/// Every strongly-typed GUID id implements <see cref="IGuidId{TSelf}"/>, which supplies a uniform
/// <see cref="IComparable{T}"/> ordering by the underlying <see cref="Guid"/> through default interface
/// members. This is what lets read queries order by the id value object directly (EF translates to ORDER BY
/// the mapped column; the in-memory provider sorts via <c>Comparer&lt;T&gt;.Default</c>, which dispatches to
/// the default member). These tests assert that dispatch matches the underlying Guid order.
/// </summary>
public sealed class GuidIdComparableTests
{
    private static readonly Guid Low = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid High = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    [Fact]
    public void Should_OrderById_When_Compared()
    {
        // Comparer<T>.Default dispatches to IComparable<T> — the same path Enumerable.OrderBy and EF's
        // in-memory provider take. The default interface member orders by the underlying Guid.
        var comparer = Comparer<UserId>.Default;

        Assert.True(comparer.Compare(UserId.From(Low), UserId.From(High)) < 0);
        Assert.True(comparer.Compare(UserId.From(High), UserId.From(Low)) > 0);
        Assert.Equal(0, comparer.Compare(UserId.From(Low), UserId.From(Low)));
    }

    [Fact]
    public void Should_SortByUnderlyingGuid_When_OrderedAsValueObject()
    {
        var ids = new[] { ProjectResourceId.From(High), ProjectResourceId.From(Low) };

        var ordered = ids.OrderBy(static id => id).ToArray();

        Assert.Equal(Low, ordered[0].Value);
        Assert.Equal(High, ordered[1].Value);
    }

    [Fact]
    public void Should_MatchGuidComparison_When_AllIdTypesCompared()
    {
        var expected = Low.CompareTo(High);

        Assert.Equal(expected, Comparer<ProjectId>.Default.Compare(ProjectId.From(Low), ProjectId.From(High)));
        Assert.Equal(expected, Comparer<ProviderId>.Default.Compare(ProviderId.From(Low), ProviderId.From(High)));
        Assert.Equal(expected, Comparer<RequestId>.Default.Compare(RequestId.From(Low), RequestId.From(High)));
        Assert.Equal(expected, Comparer<LocationId>.Default.Compare(LocationId.From(Low), LocationId.From(High)));
        Assert.Equal(expected, Comparer<ProjectResourceId>.Default.Compare(ProjectResourceId.From(Low), ProjectResourceId.From(High)));
    }
}
