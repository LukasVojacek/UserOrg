namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// The projection-layer lifecycle state of a <see cref="ProjectResourceRequest"/> (ADR-0063 D9). Stored as a
/// PascalCase string on the row. This is a deliberately new, independent set — the legacy numeric
/// <c>RequestState</c> (1–5) is NOT reused and will be discarded with the legacy path.
/// Non-terminal states (<see cref="PendingPiApproval"/>, <see cref="AwaitingResourceCreation"/>) are the ones
/// the filtered single-active unique index constrains (ADR-0066).
/// </summary>
public readonly record struct ResourceRequestState
{
    private ResourceRequestState(string value) => Value = value;

    /// <summary>Awaiting the PI's action-link decision (first gate).</summary>
    public static ResourceRequestState PendingPiApproval => new("PendingPiApproval");

    /// <summary>Approved, but the requested resource does not exist yet — parked on the second gate.</summary>
    public static ResourceRequestState AwaitingResourceCreation => new("AwaitingResourceCreation");

    /// <summary>Terminal: the resource was attached to the project.</summary>
    public static ResourceRequestState Assigned => new("Assigned");

    /// <summary>Terminal: the PI rejected the request.</summary>
    public static ResourceRequestState Rejected => new("Rejected");

    /// <summary>Terminal: an owner/manager cancelled the request.</summary>
    public static ResourceRequestState Cancelled => new("Cancelled");

    /// <summary>Terminal: the flow failed unrecoverably.</summary>
    public static ResourceRequestState Failed => new("Failed");

    /// <summary>
    /// Terminal: the request sat non-terminal past its configured expiry (DEC-7) and was swept by the system.
    /// Reaching <c>Expired</c> releases the single-active slot for its (project, provider, resource) triple
    /// because it is terminal and non-terminal states are the only ones the filtered unique index constrains
    /// (see class remarks).
    /// </summary>
    public static ResourceRequestState Expired => new("Expired");

    /// <summary>The two non-terminal states, in filtered-index order.</summary>
    public static IReadOnlyList<ResourceRequestState> NonTerminal { get; } =
        [PendingPiApproval, AwaitingResourceCreation];

    private static readonly IReadOnlyList<ResourceRequestState> All =
        [PendingPiApproval, AwaitingResourceCreation, Assigned, Rejected, Cancelled, Failed, Expired];

    /// <summary>The stored PascalCase value.</summary>
    public string Value { get; }

    /// <summary>True for every state that is not one of the <see cref="NonTerminal"/> states.</summary>
    public bool IsTerminal => !NonTerminal.Contains(this);

    /// <summary>Parses a stored value back into the value object, rejecting unknown states.</summary>
    public static ResourceRequestState From(string value)
    {
        foreach (var known in All)
        {
            if (known.Value == value)
            {
                return known;
            }
        }

        throw new ArgumentException($"Unknown resource-request state '{value}'.", nameof(value));
    }

    /// <summary>Parses a state name (case-sensitive PascalCase), returning false for unknown names; used by list filter.</summary>
    public static bool TryFrom(string? value, out ResourceRequestState state)
    {
        foreach (var known in All)
        {
            if (known.Value == value)
            {
                state = known;
                return true;
            }
        }

        state = default;
        return false;
    }

    public override string ToString() => Value;
}