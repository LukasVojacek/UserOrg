using Core.DataAccess.ResourceRequests;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Terminal cancel step (ADR-0064): clears the projection's token hash + expiry (so any outstanding action link
/// stops resolving) and records the row as <c>Cancelled</c> with the optional reason, or <c>Expired</c> when the
/// request was reaped by the expiry sweeper (DEC-7/DER-2) — the same cancel signal on either gate converges here.
/// No network calls — always succeeds.
/// </summary>
internal sealed class InvalidateLinksHandler(
    IResourceRequestProjectionStore store) : IStepHandler<InvalidateLinksStep>
{
    public async Task<StepResult> HandleAsync(InvalidateLinksStep request, CancellationToken ct)
    {
        await store.UpdateBySlugAsync(request.Slug, r =>
        {
            r.TokenHash = null;
            r.TokenExpiresAt = null;
            r.State = request.Expired ? ResourceRequestState.Expired : ResourceRequestState.Cancelled;
            r.Reason = request.Reason;
        }, ct);

        return new StepResult(null);
    }
}