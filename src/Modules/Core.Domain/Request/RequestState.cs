using System.Collections.Frozen;

namespace Core.Domain.Request;

/// <summary>
/// Request State value object wrapping int enum with state machine validation.
/// Numeric values match the persisted database schema (legacy-compatible):
///   1 = Pending, 2 = PendingForPIAccept, 3 = PendingForHPCProject, 4 = Accepted, 5 = Rejected.
/// </summary>
public readonly record struct RequestState
{
    /// <summary>
    /// Single source of truth for the value↔name mapping. <see cref="Name"/>, <see cref="TryParseName"/>, and
    /// the int factory all derive from this table so they cannot drift apart.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> _names = new Dictionary<int, string>
    {
        [1] = "Pending",
        [2] = "PendingForPIAccept",
        [3] = "PendingForHPCProject",
        [4] = "Accepted",
        [5] = "Rejected",
    };

    private static readonly FrozenSet<(int From, int To)> _allowedTransitions =
        new[] { (1, 2), (1, 4), (1, 5), (2, 3), (2, 4), (2, 5), (3, 4), (3, 5) }.ToFrozenSet();

    public int Value { get; }

    private RequestState(int value)
    {
        if (!IsValid(value))
            throw new ArgumentException($"Invalid request state: {value}. Valid range: 1-5", nameof(value));
        Value = value;
    }

    private static bool IsValid(int value) => _names.ContainsKey(value);

    public static RequestState Pending => new(1);
    public static RequestState PendingForPIAccept => new(2);
    public static RequestState PendingForHPCProject => new(3);
    public static RequestState Accepted => new(4);
    public static RequestState Rejected => new(5);

    public static RequestState Create(int value) => new(value);

    /// <summary>
    /// Stable PascalCase name for the state, emitted in (and accepted from) v2 contracts so consumers bind to
    /// a name rather than the legacy persisted int. The int stays internal to the domain/persistence.
    /// </summary>
    public string Name => _names.TryGetValue(Value, out var name) ? name : "Unknown";

    /// <summary>
    /// Parses a stable PascalCase state name (case-sensitive, as emitted by <see cref="Name"/>) back to the
    /// value object. Returns false for an unknown name without throwing.
    /// </summary>
    public static bool TryParseName(string name, out RequestState result)
    {
        foreach (var (value, candidate) in _names)
        {
            if (candidate == name)
            {
                result = new RequestState(value);
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Determines if a transition from the current state to <paramref name="newState"/> is valid.
    /// <list type="bullet">
    ///   <item>Pending → PendingForPIAccept, Accepted, or Rejected</item>
    ///   <item>PendingForPIAccept → PendingForHPCProject, Accepted, or Rejected</item>
    ///   <item>PendingForHPCProject → Accepted or Rejected</item>
    ///   <item>Accepted and Rejected are final states (no outgoing transitions)</item>
    /// </list>
    /// </summary>
    public bool CanTransitionTo(RequestState newState) => _allowedTransitions.Contains((Value, newState.Value));

    public string GetDescription() => Value switch
    {
        1 => "Pending",
        2 => "Pending for PI Accept",
        3 => "Pending for HPC Project",
        4 => "Accepted",
        5 => "Rejected",
        _ => "Unknown",
    };

    public bool IsFinalState => Value is 4 or 5;

    public static implicit operator int(RequestState state) => state.Value;
    public static explicit operator RequestState(int value) => new(value);

    public override string ToString() => GetDescription();
}