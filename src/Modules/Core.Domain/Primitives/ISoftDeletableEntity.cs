namespace Core.Domain.Primitives;

public interface ISoftDeletableEntity : IEntity
{
    public bool IsDeleted { get; }
}