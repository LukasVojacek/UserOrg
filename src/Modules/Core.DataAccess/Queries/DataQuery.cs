using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Queries;

internal sealed class DataQuery<T>(Contexts.DatabaseContext db) : IDataQuery<T> where T : class, IEntity
{
    private IQueryable<T> _readonlyQuery => db.Set<T>().AsNoTracking();

    public async Task<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        => await query(_readonlyQuery)
            .TagWith(tag)
            .TagWithCallSite()
            .FirstOrDefaultAsync(ct);

    public async Task<List<TResult>> ToListAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        => await query(_readonlyQuery)
            .TagWith(tag)
            .TagWithCallSite()
            .ToListAsync(ct);

    public async Task<QueryPage<TResult>> ToPagedListAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
    {
        var projected = query(_readonlyQuery)
            .TagWith(tag)
            .TagWithCallSite();

        var total = await projected.CountAsync(ct);
        var items = await projected
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new QueryPage<TResult>(items, total);
    }

    public async Task<TResult?> FirstOrDefaultAsync<T2, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity
        => await query(_readonlyQuery, db.Set<T2>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite()
            .FirstOrDefaultAsync(ct);

    public async Task<List<TResult>> ToListAsync<T2, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity
        => await query(_readonlyQuery, db.Set<T2>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite()
            .ToListAsync(ct);

    public async Task<QueryPage<TResult>> ToPagedListAsync<T2, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
        where T2 : class, IEntity
    {
        var projected = query(_readonlyQuery, db.Set<T2>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite();

        var total = await projected.CountAsync(ct);
        var items = await projected
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new QueryPage<TResult>(items, total);
    }

    public async Task<TResult?> FirstOrDefaultAsync<T2, T3, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity
        => await query(_readonlyQuery, db.Set<T2>().AsNoTracking(), db.Set<T3>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite()
            .FirstOrDefaultAsync(ct);

    public async Task<List<TResult>> ToListAsync<T2, T3, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity
        => await query(_readonlyQuery, db.Set<T2>().AsNoTracking(), db.Set<T3>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite()
            .ToListAsync(ct);

    public async Task<QueryPage<TResult>> ToPagedListAsync<T2, T3, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<T3>, IQueryable<TResult>> query, int page, int limit, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity
    {
        var projected = query(_readonlyQuery, db.Set<T2>().AsNoTracking(), db.Set<T3>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite();

        var total = await projected.CountAsync(ct);
        var items = await projected
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new QueryPage<TResult>(items, total);
    }

    public async Task<List<TResult>> ToListAsync<T2, T3, T4, TResult>(Func<IQueryable<T>, IQueryable<T2>, IQueryable<T3>, IQueryable<T4>, IQueryable<TResult>> query, string tag, CancellationToken ct)
        where T2 : class, IEntity where T3 : class, IEntity where T4 : class, IEntity
        => await query(_readonlyQuery, db.Set<T2>().AsNoTracking(), db.Set<T3>().AsNoTracking(), db.Set<T4>().AsNoTracking())
            .TagWith(tag)
            .TagWithCallSite()
            .ToListAsync(ct);
}