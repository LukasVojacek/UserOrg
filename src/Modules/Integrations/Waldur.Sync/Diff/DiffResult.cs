using Waldur.Contracts.Events;

namespace Waldur.Sync.Diff;

/// <summary>
/// The pure output of one <see cref="DiffEngine.Diff"/> call: the events to publish (each paired with
/// the SHA mutation to apply on ack) and the absence transitions to log. The engine mutates nothing;
/// all state changes are deferred to the worker.
/// </summary>
public sealed record DiffResult
{
    /// <summary>
    /// One action per event, in phase order (providers → projects → memberships → storage). The worker
    /// publishes <see cref="DiffAction.Event"/> and applies <see cref="DiffAction.OnAck"/> to the map
    /// only after a successful ack (ADR-0056).
    /// </summary>
    public IReadOnlyList<DiffAction> Actions { get; init; } = [];

    /// <summary>
    /// Provider/project entries that were in the SHA map but absent from this iteration's input. These
    /// are logged as transition-only warnings (ADR-0058/A1); per D3 absence produces <b>no</b> delete
    /// event. The worker removes the entry after logging so the warning fires on transition only.
    /// </summary>
    public IReadOnlyList<AbsenceTransition> Absences { get; init; } = [];
}

/// <summary>An event to publish plus the SHA mutation to apply once it is acked.</summary>
/// <param name="Event">The integration event to publish.</param>
/// <param name="OnAck">The map mutation to apply on successful ack.</param>
public sealed record DiffAction(WaldurIntegrationEvent Event, ShaMutation OnAck);

/// <summary>A provider/project that vanished from the pull while still present in the SHA map.</summary>
/// <param name="Key">The absent map entry (never a membership key — those follow the D7 rule).</param>
public sealed record AbsenceTransition(ShaKey Key);