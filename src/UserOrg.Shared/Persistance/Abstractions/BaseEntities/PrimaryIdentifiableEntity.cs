using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrg.Shared.Persistance.Abstractions.BaseEntities;

/// <summary>
/// Identifiable Database entity
/// </summary>
/// <typeparam name="T">Type</typeparam>
public abstract class PrimaryIdentifiableEntity<T> : IEntity<T> where T : struct, IEquatable<T>
{
    #region Properties
    /// <summary>
    /// Id
    /// </summary>
    public T Id { get; set; }
    #endregion
}