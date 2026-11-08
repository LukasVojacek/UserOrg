using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrg.Shared.Persistance.Abstractions.BaseEntities;

/// <summary>
/// Auditable Base Enum Database entity
/// </summary>
/// <typeparam name="T">Type</typeparam>
public abstract class AuditableEnumEntity<T> : EnumEntity<T>, IAuditableEntity where T : struct, IEquatable<T>
{
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
}