namespace Core.Features.ResourceRequest.Flow;

/// <summary>The PI's decision delivered to the <c>PiApproval</c> gate (ADR-0064).</summary>
internal enum PiDecisionKind
{
    /// <summary>PI approves — the flow looks the requested resource up and attaches or parks.</summary>
    Approve,

    /// <summary>PI rejects — the flow notifies the requester and terminates <c>Rejected</c>.</summary>
    Reject,

    /// <summary>Owner/Manager cancelled while awaiting PI approval — the flow invalidates links and terminates <c>Cancelled</c>.</summary>
    Cancel,

    /// <summary>
    /// System-initiated by the expiry sweeper only (DEC-7); never accepted from a request body. Reuses the
    /// cancel signal path (DER-2) to invalidate links and terminate <c>Expired</c> instead of <c>Cancelled</c>.
    /// </summary>
    Expire,
}

/// <summary>
/// Signal resuming the <c>PiApproval</c> gate. Carries the PI's decision and an optional reason (surfaced on
/// reject to the requester). Cancel is modelled as a decision variant so a cancel from either gate converges on
/// <c>InvalidateLinks → Terminal</c>.
/// </summary>
internal sealed record PiDecision(PiDecisionKind Decision, string? Reason = null);

/// <summary>The signal delivered to the <c>ResourceAvailability</c> gate.</summary>
internal enum ResourceAvailabilityKind
{
    /// <summary>The awaited resource now exists — attach it and notify the requester.</summary>
    Available,

    /// <summary>Owner/Manager cancelled while awaiting resource creation — invalidate links and terminate <c>Cancelled</c>.</summary>
    Cancel,

    /// <summary>
    /// System-initiated by the expiry sweeper only (DEC-7); never accepted from a request body. Reuses the
    /// cancel signal path (DER-2) to invalidate links and terminate <c>Expired</c> instead of <c>Cancelled</c>.
    /// </summary>
    Expire,
}

/// <summary>
/// Signal resuming the <c>ResourceAvailability</c> gate: either the awaited resource became available (the
/// provider created it, delivered by <c>ProjectResourceActivatedEventHandler</c>) or the request was cancelled.
/// </summary>
internal sealed record ResourceAvailabilitySignal(ResourceAvailabilityKind Signal);

/// <summary>Caller input for starting the flow (slug is the projection PK and the flow's correlation key).</summary>
internal sealed record AssignResourceFlowInput(string Slug);