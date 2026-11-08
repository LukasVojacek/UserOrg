namespace UserOrg.Shared.Persistance.Abstractions.BaseEntities;

/// <summary>
/// Base Enum Database entity
/// </summary>
/// <typeparam name="T">Type</typeparam>
public abstract class EnumEntity<T> : PrimaryIdentifiableEntity<T> where T : struct, IEquatable<T>
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}