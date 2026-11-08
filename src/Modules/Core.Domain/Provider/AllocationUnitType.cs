namespace Core.Domain.Provider;

/// <summary>
/// AllocationUnitType — reference/lookup entity (seeded reference data, read-only at runtime).
/// Id is an int identity managed by EF.
/// </summary>
public sealed class AllocationUnitType : IEntity
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private AllocationUnitType() { }
}