using Core.DataAccess.ResourceRequests;
using Core.Features.Request.Notifications;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Terminal reject step: records the projection as <c>Rejected</c> (with the PI's optional reason) and emails
/// the requester the rejection, carrying the reason. Failed email is retryable; missing requester is permanent.
/// </summary>
internal sealed class NotifyRequesterRejectedHandler(
    IResourceRequestProjectionStore store,
    IUserInfoQuery users,
    INotificationSender notifications) : IStepHandler<NotifyRequesterRejectedStep>
{
    public async Task<StepResult> HandleAsync(NotifyRequesterRejectedStep request, CancellationToken ct)
    {
        await store.UpdateBySlugAsync(request.Slug, r =>
        {
            r.State = ResourceRequestState.Rejected;
            r.Reason = request.Reason;
            // Single-use token spent on the PI decision — invalidate so it can never verify again (ADR-0064).
            r.TokenHash = null;
            r.TokenExpiresAt = null;
        }, ct);

        var row = await store.RequireBySlugAsync(request.Slug, ct);

        var requester = await users.GetBySubAsync(IdpId.Create(row.RequesterIdpId), ct)
            ?? throw new PermanentException("resource_request.requester_missing", $"Requester '{row.RequesterIdpId}' not found.");

        await FlowHandlerHelpers.SendOrRetryAsync(
            "resource_request.rejected_email_failed", "Failed to send the rejection notification.",
            () => notifications.SendRequesterRejectedAsync(
                requester.Email,
                row.Slug,
                ProjectShortName.Create(row.ProjectShortName),
                ResourceName.Create(row.ResourceName),
                request.Reason,
                ct));

        return new StepResult(null);
    }
}