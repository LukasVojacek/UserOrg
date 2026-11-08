namespace Core.DataAccess.Queries;

internal static class Grouping
{
    /// <summary>
    /// Groups <paramref name="source"/> by <paramref name="key"/> into a dictionary of read-only lists,
    /// projecting each element with <paramref name="select"/>. Pure and in-memory — call it after the rows
    /// have been materialised, never inside an EF query expression.
    /// </summary>
    internal static Dictionary<TKey, IReadOnlyList<TOut>> GroupToLists<TIn, TKey, TOut>(
        this IEnumerable<TIn> source,
        Func<TIn, TKey> key,
        Func<TIn, TOut> select) where TKey : notnull
        => source
            .GroupBy(key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TOut>)g.Select(select).ToList());
}