namespace Core.Domain.Primitives;

public interface IDataQuery<TEntity> where TEntity : IEntity
{
    Task<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, string tag, CancellationToken ct);
    Task<List<TResult>> ToListAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, string tag, CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="query"/> as one offset page: counts all matching rows, then returns the
    /// <paramref name="page"/>/<paramref name="limit"/> slice. The builder must apply its own ordering
    /// (with a unique tie-breaker) before this skips/takes, or rows shuffle across pages.
    /// </summary>
    Task<QueryPage<TResult>> ToPagedListAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct);

    Task<TResult?> FirstOrDefaultAsync<T2, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity;
    Task<List<TResult>> ToListAsync<T2, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity;

    /// <summary>
    /// Multi-entity counterpart of <see cref="ToPagedListAsync{TResult}"/>: the builder may reference one
    /// extra entity set (typically only inside <c>Where</c>/<c>EXISTS</c> filters), but the projection must
    /// still resolve to a single <see cref="IQueryable{TResult}"/> so the count and the
    /// <paramref name="page"/>/<paramref name="limit"/> slice run in SQL. The builder must apply its own
    /// ordering (with a unique tie-breaker) before this skips/takes, or rows shuffle across pages.
    /// </summary>
    Task<QueryPage<TResult>> ToPagedListAsync<T2, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
        where T2 : class, IEntity;

    Task<TResult?> FirstOrDefaultAsync<T2, T3, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity;
    Task<List<TResult>> ToListAsync<T2, T3, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity;

    /// <summary>
    /// Two-extra-entity counterpart of <see cref="ToPagedListAsync{TResult}"/>: the builder may reference two
    /// extra entity sets (typically only inside <c>Where</c>/<c>EXISTS</c> filters), but the projection must
    /// still resolve to a single <see cref="IQueryable{TResult}"/> so the count and the
    /// <paramref name="page"/>/<paramref name="limit"/> slice run in SQL. The builder must apply its own
    /// ordering (with a unique tie-breaker) before this skips/takes, or rows shuffle across pages.
    /// </summary>
    Task<QueryPage<TResult>> ToPagedListAsync<T2, T3, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity;

    Task<List<TResult>> ToListAsync<T2, T3, T4, TResult>(Func<IQueryable<TEntity>, IQueryable<T2>, IQueryable<T3>, IQueryable<T4>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity where T4 : class, IEntity;
}