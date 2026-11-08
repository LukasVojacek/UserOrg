namespace Core.Domain.Provider;

/// <summary>
/// Country — reference/lookup entity (seeded reference data, read-only at runtime).
/// Id is an int identity managed by EF.
/// </summary>
public sealed class Country : IEntity
{
    public int Id { get; private set; }
    public CountryName Name { get; private set; }
    public string? Description { get; private set; }

    private Country() { }
}