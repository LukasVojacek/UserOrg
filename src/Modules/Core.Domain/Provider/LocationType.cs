using Core.Domain.Shared;

namespace Core.Domain.Provider;

/// <summary>
/// LocationType — reference/lookup entity (seeded reference data, read-only at runtime).
/// Id is the <see cref="ELocationType"/> value, stored as an int identity managed by EF.
/// </summary>
public sealed class LocationType : IEntity
{
    public ELocationType Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private LocationType() { }
}