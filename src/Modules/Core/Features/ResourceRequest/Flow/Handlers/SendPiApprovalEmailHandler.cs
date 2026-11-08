using Core.DataAccess.ResourceRequests;
using Core.Domain.Shared;
using Core.Features.Request.Notifications;

using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Start step (ADR-0064): mints a single-use PI action-link token, stores its SHA-256 hash + TTL on the
/// projection row, sets the row <c>PendingPiApproval</c>, and emails the PI the approval link. The plaintext
/// token is never persisted. Failed email is retryable; missing requester is permanent.
/// </summary>
internal sealed class SendPiApprovalEmailHandler(
    IResourceRequestProjectionStore store,
    INotificationSender notifications,
    IOptions<ResourceRequestActionLinkOptions> options) : IStepHandler<SendPiApprovalEmailStep>
{
    public async Task<StepResult> HandleAsync(SendPiApprovalEmailStep request, CancellationToken ct)
    {
        var row = await store.RequireBySlugAsync(request.Slug, ct);

        var token = ResourceRequestToken.Generate();
        var hash = ResourceRequestToken.Hash(token);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(options.Value.DurationInHours);

        await store.UpdateBySlugAsync(request.Slug, r =>
        {
            r.TokenHash = hash;
            r.TokenExpiresAt = expiresAt;
            r.WorkflowRequestId = request.RequestId;
            r.State = ResourceRequestState.PendingPiApproval;
        }, ct);

        await FlowHandlerHelpers.SendOrRetryAsync(
            "resource_request.pi_email_failed", "Failed to send the PI approval email.",
            () => notifications.SendPiApprovalRequestedAsync(
                Email.Create(row.ContactEmail),
                row.Slug,
                token,
                ProjectShortName.Create(row.ProjectShortName),
                ResourceName.Create(row.ResourceName),
                ct));

        return new StepResult(null);
    }
}