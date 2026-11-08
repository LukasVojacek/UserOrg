using System.Linq.Expressions;

using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

/// <summary>
/// IRepository
/// </summary>
/// <typeparam name="T">Entity</typeparam>
/// <typeparam name="U">Type of primary key</typeparam>
public interface IRepository<T, TU> where T : PrimaryIdentifiableEntity<TU> where TU : struct, IEquatable<TU>
{
    T GetById(TU id, params Expression<Func<T, object>>[] includes);
    IList<T> GetAll(params Expression<Func<T, object>>[] includes);
    void Insert(T entity);
    void InsertRange(IList<T> entities);
    void Delete(TU id);
    void Delete(T entityToDelete);
    void DeleteRange(IList<T> entitiesToDelete);
    void Update(T entityToUpdate);
    void UpdateRange(IList<T> entitiesToUpdate);
    Task<T> GetByIdAsync(TU id, CancellationToken cancelationToken, params Expression<Func<T, object>>[] includes);
    Task<IList<T>> GetAllAsync(CancellationToken cancelationToken, params Expression<Func<T, object>>[] includes);
    Task InsertAsync(T entity, CancellationToken cancelationToken);
    Task InsertRangeAsync(IList<T> entities, CancellationToken cancelationToken);
    Task DeleteAsync(TU id, CancellationToken cancelationToken);
}