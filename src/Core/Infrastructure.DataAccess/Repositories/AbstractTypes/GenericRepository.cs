using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared.Persistance.Abstractions.BaseEntities;
using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

/// <summary>
/// Generic repository
/// </summary>
/// <typeparam name="T">Generic type</typeparam>
/// <typeparam name="U">Type of primary key</typeparam>
/// <remarks>
/// Constructor
/// </remarks>
/// <param name="context">Context</param>
public class GenericRepository<T, TU>(DatabaseContext context) : IRepository<T, TU> where T : PrimaryIdentifiableEntity<TU> where TU : struct, IEquatable<TU>
{
    #region Instances
    /// <summary>
    /// Context
    /// </summary>
    protected DatabaseContext Context { get; } = context;

    protected DbSet<T> DbSet { get; } = context?.Set<T>();

    #endregion
    #region Constructors
    #endregion
    #region Methods
    /// <summary>
    /// Get record by Id
    /// </summary>
    /// <param name="id">Id</param>
    /// <param name="includes">Includes</param>
    /// <returns></returns>
    public virtual T GetById(TU id, params Expression<Func<T, object>>[] includes)
    {
        ArgumentNullException.ThrowIfNull(includes);
        IQueryable<T> dbSet = DbSet;
        if (typeof(T).GetInterface("IAuditableEntity") is not null)
        {
            dbSet = DbSet.Where(w => !((IAuditableEntity)w).IsDeleted);
        }

        foreach (Expression<Func<T, object>> include in includes)
        {
            dbSet = dbSet.Include(include);
        }

        if (includes.Length > 0)
        {
            dbSet = dbSet.AsSplitQuery();
        }

        return dbSet.FirstOrDefault(f => f.Id.Equals(id));
    }

    /// <summary>
    /// Get all records
    /// </summary>
    /// <param name="includes">Includes</param>
    /// <returns></returns>
    public virtual IList<T> GetAll(params Expression<Func<T, object>>[] includes)
    {
        ArgumentNullException.ThrowIfNull(includes);
        IQueryable<T> dbSet = DbSet;
        if (typeof(T).GetInterface("IAuditableEntity") is not null)
        {
            dbSet = DbSet.Where(w => !((IAuditableEntity)w).IsDeleted);
        }

        foreach (Expression<Func<T, object>> include in includes)
        {
            dbSet = dbSet.Include(include);
        }

        if (includes.Length > 0)
        {
            dbSet = dbSet.AsSplitQuery();
        }

        return dbSet.ToList();
    }

    /// <summary>
    /// Insert record
    /// </summary>
    /// <param name="entity">Entity</param>
    public virtual void Insert(T entity) => DbSet.Add(entity);

    /// <summary>
    /// Insert multiple records
    /// </summary>
    /// <param name="entities">Entities</param>
    public virtual void InsertRange(IList<T> entities) => DbSet.AddRange(entities);

    /// <summary>
    /// Delete record
    /// </summary>
    /// <param name="id">Id</param>
    public virtual void Delete(TU id)
    {
        T entityToDelete = DbSet.Find(id);
        Delete(entityToDelete);
    }

    /// <summary>
    /// Delete entity record
    /// </summary>
    /// <param name="entityToDelete">Entity</param>
    public virtual void Delete(T entityToDelete)
    {
        if (Context.Entry(entityToDelete).State == EntityState.Detached)
        {
            DbSet.Attach(entityToDelete);
        }

        DbSet.Remove(entityToDelete);
    }

    /// <summary>
    /// Delete entities records
    /// </summary>
    /// <param name="entitiesToDelete">Entities</param>
    public virtual void DeleteRange(IList<T> entitiesToDelete)
    {
        ArgumentNullException.ThrowIfNull(entitiesToDelete);
        List<T> detachedEntities = [];
        foreach (T entityToDelete in entitiesToDelete)
        {
            if (Context.Entry(entityToDelete).State == EntityState.Detached)
            {
                detachedEntities.Add(entityToDelete);
            }
        }

        DbSet.AttachRange(detachedEntities);
        DbSet.RemoveRange(detachedEntities);
    }

    /// <summary>
    /// Update entity record
    /// </summary>
    /// <param name="entityToUpdate">Entity</param>
    public virtual void Update(T entityToUpdate)
    {
        DbSet.Attach(entityToUpdate);
        Context.Entry(entityToUpdate).State = EntityState.Modified;
        Context.SaveChanges();
    }

    /// <summary>
    /// Update entities records
    /// </summary>
    /// <param name="entitiesToUpdate">Entities</param>
    public virtual void UpdateRange(IList<T> entitiesToUpdate)
    {
        ArgumentNullException.ThrowIfNull(entitiesToUpdate);
        DbSet.AttachRange(entitiesToUpdate);
        foreach (T entityToUpdate in entitiesToUpdate)
        {
            Context.Entry(entityToUpdate).State = EntityState.Modified;
        }
    }
    #endregion
    #region Async Methods
    /// <summary>
    /// Get record by Id asynchronously
    /// </summary>
    /// <param name="id">Id</param>
    /// <param name="cancelationToken">Cancelation token</param>
    /// <returns></returns>
    public virtual async Task<T> GetByIdAsync(TU id, CancellationToken cancelationToken, params Expression<Func<T, object>>[] includes)
    {
        ArgumentNullException.ThrowIfNull(includes);
        IQueryable<T> dbSet = DbSet;
        if (typeof(T).GetInterface("IAuditableEntity") is not null)
        {
            dbSet = DbSet.Where(w => !((IAuditableEntity)w).IsDeleted);
        }

        foreach (Expression<Func<T, object>> include in includes)
        {
            dbSet = dbSet.Include(include);
        }

        if (includes.Length > 0)
        {
            dbSet = dbSet.AsSplitQuery();
        }

        return await dbSet.FirstOrDefaultAsync(f => f.Id.Equals(id), cancelationToken);
    }

    /// <summary>
    /// Get all records asynchronously
    /// </summary>
    /// <param name="cancelationToken">Cancelation token</param>
    /// <param name="includes">Includes</param>
    /// <returns></returns>
    public virtual async Task<IList<T>> GetAllAsync(CancellationToken cancelationToken, params Expression<Func<T, object>>[] includes)
    {
        ArgumentNullException.ThrowIfNull(includes);
        IQueryable<T> dbSet = DbSet;
        if (typeof(T).GetInterface("IAuditableEntity") is not null)
        {
            dbSet = DbSet.Where(w => !((IAuditableEntity)w).IsDeleted);
        }

        foreach (Expression<Func<T, object>> include in includes)
        {
            dbSet = dbSet.Include(include);
        }

        if (includes.Length > 0)
        {
            dbSet = dbSet.AsSplitQuery();
        }

        return await dbSet.ToListAsync(cancelationToken);
    }

    /// <summary>
    /// Insert record asynchronously
    /// </summary>
    /// <param name="entity">Entity</param>
    /// <param name="cancelationToken">Cancelation token</param>
    /// <returns></returns>
    public virtual async Task InsertAsync(T entity, CancellationToken cancelationToken) => await DbSet.AddAsync(entity, cancelationToken);

    /// <summary>
    /// Insert multiple records asynchronously
    /// </summary>
    /// <param name="entities">Entities</param>
    /// <param name="cancelationToken">Cancelation token</param>
    /// <returns></returns>
    public virtual async Task InsertRangeAsync(IList<T> entities, CancellationToken cancelationToken) => await DbSet.AddRangeAsync(entities, cancelationToken);

    /// <summary>
    /// Delete record asynchronously
    /// </summary>
    /// <param name="id">Id</param>
    /// <param name="cancelationToken">Cancelation token</param>
    /// <returns></returns>
    public virtual async Task DeleteAsync(TU id, CancellationToken cancelationToken)
    {
        T entityToDelete = await DbSet.FindAsync([id, cancelationToken], cancellationToken: cancelationToken);
        Delete(entityToDelete);
    }
    #endregion
}