namespace Core.Domain.OnboardingRequest;

/// <summary>
/// OnboardingRequest state value object wrapping an int enum with state-machine validation.
/// Numeric values match the persisted schema: 0 = Pending (default), 1 = Accepted, 2 = Rejected.
/// Accepted and Rejected are terminal.
/// </summary>
public readonly record struct OnboardingRequestState : IComparable<OnboardingRequestState>
{
    /// <summary>
    /// Single source of truth for the value↔name mapping. <see cref="Name"/>, <see cref="TryParseName"/>, and
    /// the int factory all derive from this table so they cannot drift apart.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> _names = new Dictionary<int, string>
    {
        [0] = "Pending",
        [1] = "Accepted",
        [2] = "Rejected",
    };

    public int Value { get; }

    private OnboardingRequestState(int value)
    {
        if (!IsValid(value))
            throw new ArgumentException($"Invalid onboarding request state: {value}. Valid range: 0-2", nameof(value));
        Value = value;
    }

    private static bool IsValid(int value) => _names.ContainsKey(value);

    public static OnboardingRequestState Pending => new(0);
    public static OnboardingRequestState Accepted => new(1);
    public static OnboardingRequestState Rejected => new(2);

    public static OnboardingRequestState Create(int value) => new(value);

    /// <summary>
    /// Stable PascalCase name for the state, emitted in (and accepted from) v2 contracts so consumers bind to
    /// a name rather than the persisted int.
    /// </summary>
    public string Name => _names.TryGetValue(Value, out var name) ? name : "Unknown";

    /// <summary>
    /// Parses a stable PascalCase state name (case-sensitive, as emitted by <see cref="Name"/>) back to the
    /// value object. Returns false for an unknown name without throwing.
    /// </summary>
    public static bool TryParseName(string name, out OnboardingRequestState result)
    {
        foreach (var (value, candidate) in _names)
        {
            if (candidate == name)
            {
                result = new OnboardingRequestState(value);
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Determines if a transition from the current state to <paramref name="newState"/> is valid.
    /// <list type="bullet">
    ///   <item>Pending → Accepted or Rejected</item>
    ///   <item>Accepted and Rejected are terminal (no outgoing transitions)</item>
    /// </list>
    /// </summary>
    public bool CanTransitionTo(OnboardingRequestState newState)
    {
        return (Value, newState.Value) switch
        {
            (0, 1) => true,  // Pending → Accepted
            (0, 2) => true,  // Pending → Rejected
            _ => false,
        };
    }

    public bool IsFinalState => Value is 1 or 2;

    public int CompareTo(OnboardingRequestState other) => Value.CompareTo(other.Value);

    public static implicit operator int(OnboardingRequestState state) => state.Value;
    public static explicit operator OnboardingRequestState(int value) => new(value);

    public override string ToString() => Name;
}