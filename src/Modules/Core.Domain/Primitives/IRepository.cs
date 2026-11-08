namespace Core.Domain.Primitives;

public interface IRepository<TAggregate, TKey> where TAggregate : AggregateRoot
{
    public ValueTask<TAggregate?> FindAsync(TKey key, CancellationToken cancellationToken);
    public ValueTask<TAggregate?> AddAsync(TAggregate aggregate, CancellationToken cancellationToken);
    void Delete(TAggregate aggregate, CancellationToken cancellationToken);

    public async ValueTask DeleteAsync(TKey key, CancellationToken cancellationToken)
    {
        var aggregate = await FindAsync(key, cancellationToken) ?? throw new ArgumentException("Key not found");
        Delete(aggregate, cancellationToken);
    }
}