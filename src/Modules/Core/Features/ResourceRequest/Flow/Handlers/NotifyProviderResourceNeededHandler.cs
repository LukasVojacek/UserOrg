using Core.DataAccess.ResourceRequests;
using Core.Features.Request.Notifications;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Wait path (DEC-3): emails the provider's support address — resolved from the <see cref="Provider"/> aggregate,
/// never from the resource, which by definition may not exist yet — that an approved request awaits creation of
/// the named resource (with the requester's contact as CC), then parks the projection at
/// <c>AwaitingResourceCreation</c> as its LAST action — the state write comes after the send so a transport retry
/// never leaves the row advertising a gate the engine has not reached yet. Even so, the engine only parks on the
/// <c>ResourceAvailability</c> gate once this handler returns, so a resume racing that residual window can arrive
/// before the gate is live; resumers (B08) must retry on the engine's "not awaiting signal".
/// Missing provider, missing support email, or failed email are handled according to <see cref="FlowHandlerHelpers.SendOrRetryAsync"/> (validation errors permanent, transport retryable).
/// </summary>
internal sealed class NotifyProviderResourceNeededHandler(
    IResourceRequestProjectionStore store,
    IProviderRepository providers,
    IUserInfoQuery users,
    INotificationSender notifications) : IStepHandler<NotifyProviderResourceNeededStep>
{
    public async Task<StepResult> HandleAsync(NotifyProviderResourceNeededStep request, CancellationToken ct)
    {
        var row = await store.RequireBySlugAsync(request.Slug, ct);

        var provider = await providers.FindAsync(ProviderName.Create(row.ProviderName), ct)
            ?? throw new PermanentException("resource_request.provider_missing", $"Provider '{row.ProviderName}' not found.");

        var support = provider.SupportEmail
            ?? throw new PermanentException("resource_request.provider_no_support_email", $"Provider '{row.ProviderName}' has no support email.");

        var requester = await users.GetBySubAsync(IdpId.Create(row.RequesterIdpId), ct)
            ?? throw new PermanentException("resource_request.requester_missing", $"Requester '{row.RequesterIdpId}' not found.");

        await FlowHandlerHelpers.SendOrRetryAsync(
            "resource_request.provider_email_failed", "Failed to send the provider notification.",
            () => notifications.SendProviderResourceNeededAsync(
                support,
                row.Slug,
                ProjectShortName.Create(row.ProjectShortName),
                ResourceName.Create(row.ResourceName),
                requester.Email,
                ct));

        // State write is the handler's last action (see remarks): only now advertise AwaitingResourceCreation.
        await store.UpdateBySlugAsync(request.Slug, r => r.State = ResourceRequestState.AwaitingResourceCreation, ct);

        return new StepResult(null);
    }
}