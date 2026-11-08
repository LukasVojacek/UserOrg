namespace Core.Domain.Primitives;

public interface IValueObject<TSelf, TValue> where TSelf : struct, IValueObject<TSelf, TValue>
{
    TValue Value { get; }
    static abstract TSelf Create(TValue value);
    static abstract TSelf From(TValue value);
}