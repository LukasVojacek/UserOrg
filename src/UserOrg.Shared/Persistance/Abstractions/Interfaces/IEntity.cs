namespace UserOrg.Shared.Persistance.Abstractions.Interfaces;

public interface IEntity<T> where T : struct, IEquatable<T>
{
    T Id { get; }
}