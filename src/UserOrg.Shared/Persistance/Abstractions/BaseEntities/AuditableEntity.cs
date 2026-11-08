using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrg.Shared.Persistance.Abstractions.BaseEntities;

/// <summary>
/// Auditable Database entity
/// </summary>
/// <typeparam name="T">Type</typeparam>
public abstract class AuditableEntity<T> : PrimaryIdentifiableEntity<T>, IAuditableEntity where T : struct, IEquatable<T>
{
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
}