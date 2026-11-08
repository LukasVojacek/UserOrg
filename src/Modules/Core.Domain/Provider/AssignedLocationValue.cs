namespace Core.Domain.Provider;

/// <summary>
/// AssignedLocationValue — child entity of <see cref="AssignedLocation"/>.
/// Id and the AssignedLocationId FK are EF shadow properties.
/// The underlying table has no audit columns.
/// </summary>
public sealed class AssignedLocationValue : IEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;

    private AssignedLocationValue() { }

    public static AssignedLocationValue Create(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        return new AssignedLocationValue
        {
            Name = name,
            Value = value,
        };
    }

    public void UpdateValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }
}