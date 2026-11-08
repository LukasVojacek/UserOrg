namespace Core.Domain.Primitives;

/// <summary>
/// A strongly-typed GUID id value object. Adds uniform ordering on top of <see cref="IValueObject{TSelf,Guid}"/>
/// via default interface members, so every id can be used in `orderby` (EF translates to ORDER BY the mapped
/// column; the in-memory provider sorts via this comparison). Ordering by `.Value` is NOT SQL-translatable.
/// </summary>
public interface IGuidId<TSelf> : IValueObject<TSelf, Guid>, IComparable<TSelf>, IComparable
    where TSelf : struct, IGuidId<TSelf>
{
    int IComparable<TSelf>.CompareTo(TSelf other) => Value.CompareTo(other.Value);

    int IComparable.CompareTo(object? obj) => obj is TSelf other
        ? ((IComparable<TSelf>)this).CompareTo(other)
        : throw new ArgumentException($"Object must be of type {typeof(TSelf).Name}.", nameof(obj));
}