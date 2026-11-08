using System.Linq.Expressions;
using System.Reflection;

using Core.DataAccess.Queries;

namespace Core.UnitTests.Queries;

/// <summary>
/// Tests for <c>QueryableExtensions.WhereIf</c>, the optional-filter helper of the Core.DataAccess read queries.
/// The helper is resolved by name, so until it exists these tests fail on a named assertion instead of breaking
/// the build of the whole test project.
/// </summary>
public sealed class QueryableExtensionsTests
{
    private static readonly int[] Numbers = [1, 2, 3, 4];

    [Fact]
    public void WhereIf_applies_the_predicate_when_the_condition_is_true()
    {
        var source = Numbers.AsQueryable();

        var result = WhereIf(source, true, n => n % 2 == 0);

        Assert.Equal([2, 4], result.ToArray());
    }

    [Fact]
    public void WhereIf_returns_the_source_itself_when_the_condition_is_false()
    {
        var source = Numbers.AsQueryable();

        var result = WhereIf(source, false, n => n % 2 == 0);

        Assert.Same(source, result);
    }

    private static IQueryable<int> WhereIf(IQueryable<int> source, bool condition, Expression<Func<int, bool>> predicate)
    {
        var type = typeof(DataQuery<>).Assembly.GetType("Core.DataAccess.Queries.QueryableExtensions");
        Assert.True(type is not null, "Core.DataAccess declares no Core.DataAccess.Queries.QueryableExtensions");

        var method = type.GetMethod("WhereIf", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(method is not null, "QueryableExtensions declares no WhereIf");

        return (IQueryable<int>)method.MakeGenericMethod(typeof(int)).Invoke(null, [source, condition, predicate])!;
    }
}
