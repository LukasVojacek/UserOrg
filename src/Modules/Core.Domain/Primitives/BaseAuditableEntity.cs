namespace Core.Domain.Primitives;

/// <summary>
/// Base class for entities tracked by EF Core.
///
/// DESIGN NOTE (DDD): Audit properties (CreatedAt, ModifiedAt) are managed as EF shadow properties,
/// not exposed in the domain model. This keeps persistence concerns out of domain code while
/// allowing EF to track entity changes automatically via DB default functions.
/// </summary>
public class BaseAuditableEntity : IEntity
{
}