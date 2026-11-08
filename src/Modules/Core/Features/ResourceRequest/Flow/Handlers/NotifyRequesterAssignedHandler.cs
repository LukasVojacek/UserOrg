using Core.DataAccess.ResourceRequests;
using Core.Features.Request.Notifications;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Notifies the requester (resolved from <see cref="ProjectResourceRequest.RequesterIdpId"/> via
/// <see cref="IUserInfoQuery"/>) that the resource was assigned. The projection is already <c>Assigned</c>
/// (set by <see cref="AttachResourceHandler"/>); this step only emails. Failed email is retryable; missing requester is permanent.
/// </summary>
internal sealed class NotifyRequesterAssignedHandler(
    IResourceRequestProjectionStore store,
    IUserInfoQuery users,
    INotificationSender notifications) : IStepHandler<NotifyRequesterAssignedStep>
{
    public async Task<StepResult> HandleAsync(NotifyRequesterAssignedStep request, CancellationToken ct)
    {
        var row = await store.RequireBySlugAsync(request.Slug, ct);

        var requester = await users.GetBySubAsync(IdpId.Create(row.RequesterIdpId), ct)
            ?? throw new PermanentException("resource_request.requester_missing", $"Requester '{row.RequesterIdpId}' not found.");

        await FlowHandlerHelpers.SendOrRetryAsync(
            "resource_request.assigned_email_failed", "Failed to send the assignment notification.",
            () => notifications.SendRequesterAssignedAsync(
                requester.Email,
                row.Slug,
                ProjectShortName.Create(row.ProjectShortName),
                ResourceName.Create(row.ResourceName),
                ct));

        return new StepResult(null);
    }
}