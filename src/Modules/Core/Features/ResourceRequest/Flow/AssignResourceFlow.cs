using Core.Features.ResourceRequest.Flow.Handlers;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.ResourceRequest.Flow;

/// <summary>
/// The <c>assign-resource-to-project</c> flow — the platform's first fluent-DSL gated flow.
/// The PI approves first (DEC-3, variant B); only then does the flow look the requested resource up. A resource
/// that does not exist yet parks on a second gate until the provider creates it. A cancel from either gate
/// converges on <c>InvalidateLinks → Terminal</c>; the expiry sweeper (DEC-7) reuses the same signal type with
/// an <c>Expire</c> decision, converging on the same step but landing <c>Expired</c> instead of <c>Cancelled</c>.
/// </summary>
/// <remarks>
/// Step and gate chain (ADR-0063/0064):
/// <code>
/// SendPiApprovalEmail → Gate&lt;PiDecision&gt;("PiApproval")
///   Approve       → CheckResourceExists
///                      exists  → AttachResource → NotifyRequesterAssigned → Terminal (Assigned)
///                      missing → NotifyProviderResourceNeeded → Gate&lt;ResourceAvailabilitySignal&gt;("ResourceAvailability")
///   Reject        → NotifyRequesterRejected → Terminal                                (Rejected)
///   Cancel/Expire → InvalidateLinks → Terminal                                        (Cancelled/Expired)
/// Gate("ResourceAvailability")
///   Available     → AttachResource → NotifyRequesterAssigned → Terminal               (Assigned)
///   Cancel/Expire → InvalidateLinks → Terminal                                        (Cancelled/Expired)
/// </code>
/// See ADR-0063 (resource-request projection), ADR-0064 (PI approval token + gate).
/// </remarks>
internal sealed class AssignResourceFlow : IFlowDefinition
{
    internal const string FlowName = "assign-resource-to-project";

    public StepRequest CreateInitialStep(Guid requestId, object input)
    {
        var typed = input as AssignResourceFlowInput
            ?? throw new ArgumentException($"Expected {nameof(AssignResourceFlowInput)}.", nameof(input));
        return new SendPiApprovalEmailStep(requestId, typed.Slug);
    }

    public void Define(IFlowBuilder flow) => flow
        .Named(FlowName)
        .Step<SendPiApprovalEmailStep, SendPiApprovalEmailHandler>()
        .Step<CheckResourceExistsStep, CheckResourceExistsHandler>()
        .Step<AttachResourceStep, AttachResourceHandler>()
        .Step<NotifyRequesterAssignedStep, NotifyRequesterAssignedHandler>()
        .Step<NotifyRequesterRejectedStep, NotifyRequesterRejectedHandler>()
        .Step<NotifyProviderResourceNeededStep, NotifyProviderResourceNeededHandler>()
        .Step<InvalidateLinksStep, InvalidateLinksHandler>()
        .Gate<PiDecision>("PiApproval")
        .Gate<ResourceAvailabilitySignal>("ResourceAvailability")

        .From<SendPiApprovalEmailStep>().GoToGate("PiApproval")

        .FromGate<PiDecision>("PiApproval").Branch(c => c
            .When(
                d => d.Decision == PiDecisionKind.Approve,
                (_, snap) => new CheckResourceExistsStep(snap.RequestId, SlugOf(snap)))
            .When(
                d => d.Decision == PiDecisionKind.Reject,
                (d, snap) => new NotifyRequesterRejectedStep(snap.RequestId, SlugOf(snap), d.Reason))
            .Otherwise(
                (d, snap) => new InvalidateLinksStep(
                    snap.RequestId, SlugOf(snap), d.Reason, Expired: d.Decision == PiDecisionKind.Expire)))

        .From<CheckResourceExistsStep>().Branch(c => c
            .When(
                (_, result) => CheckResourceExistsResult.HasResource(result),
                (req, _, _) => new AttachResourceStep(req.RequestId, req.Slug))
            .Otherwise(
                (req, _, _) => new NotifyProviderResourceNeededStep(req.RequestId, req.Slug)))

        .From<NotifyProviderResourceNeededStep>().GoToGate("ResourceAvailability")

        .FromGate<ResourceAvailabilitySignal>("ResourceAvailability").Branch(c => c
            .When(
                s => s.Signal == ResourceAvailabilityKind.Available,
                (_, snap) => new AttachResourceStep(snap.RequestId, SlugOf(snap)))
            .Otherwise(
                (s, snap) => new InvalidateLinksStep(
                    snap.RequestId, SlugOf(snap), null, Expired: s.Signal == ResourceAvailabilityKind.Expire)))

        .From<AttachResourceStep>().GoTo((req, _, _) => new NotifyRequesterAssignedStep(req.RequestId, req.Slug))

        .From<NotifyRequesterAssignedStep>().Terminal()
        .From<NotifyRequesterRejectedStep>().Terminal()
        .From<InvalidateLinksStep>().Terminal();

    // The slug is carried on the initial step, persisted as the request payload — the correlation key the gate
    // transitions read to rebuild the next step message. Pure: reads immutable, replay-safe data.
    private static string SlugOf(IRequestSnapshot snapshot) =>
        snapshot.Payload.RootElement.GetProperty(nameof(AssignResourceFlowInput.Slug)).GetString()
        ?? throw new InvalidOperationException("Request payload is missing the slug.");
}