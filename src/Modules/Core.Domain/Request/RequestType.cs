namespace Core.Domain.Request;

/// <summary>
/// Request Type value object wrapping int enum.
/// Provides type safety and validation for request types.
/// </summary>
public readonly record struct RequestType
{
    /// <summary>
    /// Single source of truth for the value↔name mapping. <see cref="Name"/> and <see cref="TryParseName"/>
    /// both derive from this table so they cannot drift apart. Note: the int factory accepts the wider
    /// legacy range 0-10 (see <see cref="IsValid"/>); only these values carry a stable wire name.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> _names = new Dictionary<int, string>
    {
        [0] = "Default",
        [1] = "SelfOnboarding",
        [2] = "ProjectResourceAssignment",
        [3] = "HEAppECreation",
    };

    public int Value { get; }

    private RequestType(int value)
    {
        if (!IsValid(value))
            throw new ArgumentException($"Invalid request type: {value}. Valid range: 0-10", nameof(value));
        Value = value;
    }

    private static bool IsValid(int value) => value >= 0 && value <= 10;

    public static RequestType Default => new(0);

    /// <summary>A request whose fulfilment is the requester joining the project as a member.</summary>
    public static RequestType SelfOnboarding => new(1);

    /// <summary>A request to attach a compute/storage resource to the project (provider-side path).</summary>
    public static RequestType ProjectResourceAssignment => new(2);

    /// <summary>A request to provision a HEAppE compute allocation for the project.</summary>
    public static RequestType HEAppECreation => new(3);

    public static RequestType Create(int value) => new(value);

    public static bool TryCreate(int value, out RequestType result)
    {
        if (IsValid(value))
        {
            result = new RequestType(value);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Stable PascalCase name for the type, emitted in (and accepted from) v2 contracts so consumers bind to
    /// a name rather than the legacy persisted int. The int stays internal to the domain/persistence.
    /// </summary>
    public string Name => _names.TryGetValue(Value, out var name) ? name : "Unknown";

    /// <summary>
    /// Parses a stable PascalCase type name (case-sensitive, as emitted by <see cref="Name"/>) back to the
    /// value object. Returns false for an unknown name without throwing.
    /// </summary>
    public static bool TryParseName(string name, out RequestType result)
    {
        foreach (var (value, candidate) in _names)
        {
            if (candidate == name)
            {
                result = new RequestType(value);
                return true;
            }
        }

        result = default;
        return false;
    }

    public static implicit operator int(RequestType type) => type.Value;
    public static explicit operator RequestType(int value) => new(value);

    public override string ToString() => Value.ToString();
}