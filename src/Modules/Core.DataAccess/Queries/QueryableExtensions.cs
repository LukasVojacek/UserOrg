using System.Linq.Expressions;

namespace Core.DataAccess.Queries;

internal static class QueryableExtensions
{
    internal static IQueryable<T> WhereIf<T>(this IQueryable<T> source, bool condition, Expression<Func<T, bool>> predicate) =>
        condition ? source.Where(predicate) : source;
}