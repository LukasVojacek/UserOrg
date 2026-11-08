using Microsoft.Extensions.Options;

using OpenTelemetry.Resources;

using UserOrg.Shared.Email;

namespace Core.Features.Request.Notifications;

/// <summary>
/// <see cref="INotificationSender"/> over the shared <see cref="IEmailSender"/> port. Maps each
/// notification onto an <see cref="EmailMessage"/> and delegates delivery; the port's best-effort
/// contract (failures logged, never thrown; cancellation still propagates) covers this sender too, so
/// it adds no failure handling of its own.
/// </summary>
internal sealed class MailNotificationSender(
    IEmailSender emailSender,
    IOptions<ResourceRequestActionLinkOptions> actionLinkOptions) : INotificationSender
{
    private readonly ResourceRequestActionLinkOptions _actionLinkOptions = actionLinkOptions.Value;

    public Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) =>
        SendAsync(requesterEmail, "NewUserAssignment", [requestId.ToString(), project.Value], null, ct);

    public Task SendRequestCreatedToOwnerAsync(Email ownerEmail, Guid requestId, ProjectShortName project, CancellationToken ct) =>
        SendAsync(ownerEmail, "NewUserAssignment_PM", [requestId.ToString(), project.Value], null, ct);

    public Task SendRequestAcceptedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) =>
        SendAsync(requesterEmail, "RequestState", [requestId.ToString(), project.Value, RequestState.Accepted.GetDescription()], null, ct);

    public Task SendRequestRejectedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) =>
        SendAsync(requesterEmail, "RequestState", [requestId.ToString(), project.Value, RequestState.Rejected.GetDescription()], null, ct);

    public Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, string actionToken, ResourceName resourceName, CancellationToken ct) =>
        SendAsync(requesterEmail, "NewResourceCreated", [
            requestId.ToString(),
             _actionLinkOptions.BuildActionLink(actionToken),
            resourceName.Value], null, ct);

    // Legacy NewProjectAssignment_PM fills 4 positional slots [Id, PortalAddress/link, ProjectShortName, validForHours]
    // (ProjectRequestService.cs:132). Resource context is folded into the project-short-name slot rather than added
    // as a positional arg, so the external template renders the remaining slots correctly.
    public Task SendPiApprovalRequestedAsync(Email piEmail, string requestSlug, string actionToken, ProjectShortName project, ResourceName resource, CancellationToken ct) =>
        SendAsync(
            piEmail,
            "NewProjectAssignment_PM",
            [requestSlug, _actionLinkOptions.BuildActionLink(actionToken), resource.Value, _actionLinkOptions.DurationInHours.ToString()],
            null,
            ct);

    // Legacy RequestState fills 3 positional slots [Id, ProjectShortName, State] (ProjectRequestService.cs:170).
    public Task SendRequesterAssignedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, CancellationToken ct) =>
        SendAsync(requesterEmail, "RequestState", [requestSlug, $"{project.Value} / {resource.Value}", "Assigned"], null, ct);

    // Reason is folded into the State slot to preserve the legacy 3-arg RequestState contract.
    public Task SendRequesterRejectedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, string? reason, CancellationToken ct) =>
        SendAsync(
            requesterEmail,
            "RequestState",
            [requestSlug, $"{project.Value} / {resource.Value}", string.IsNullOrWhiteSpace(reason) ? "Rejected" : $"Rejected: {reason}"],
            null,
            ct);

    public Task SendProviderResourceNeededAsync(Email providerSupportEmail, string requestSlug, ProjectShortName project, ResourceName resource, Email requesterContact, CancellationToken ct) =>
        SendAsync(providerSupportEmail, "NewProjectAssignment", [requestSlug, project.Value, resource.Value], requesterContact, ct);

    public Task SendResourcePublishedAsync(Email recipientEmail, string resourceName, string projectShortName, CancellationToken ct)
    {
        return SendAsync(recipientEmail, "NewResourcePublished", [resourceName, projectShortName], null, ct);
    }

    public Task SendRequestToCreateAccountAsync(Email recipientEmail, string resourceName, List<(string, string, string, string?)> clusters, CancellationToken ct)
    {
        string clusterList = string.Join("\n", clusters.Select(c => $"Cluster: {c.Item1}, Aggregation: {c.Item2}, User: {c.Item3}, PublicKey: {c.Item4}"));
        return SendAsync(recipientEmail, "NewResourceRequestAccount", [resourceName, clusterList], null, ct);
    }


    private Task SendAsync(Email receiver, string templateName, string[] templateArguments, Email? ccRecipient, CancellationToken ct) =>
        emailSender.SendAsync(
            new EmailMessage(
                templateName,
                [receiver.Value],
                templateArguments,
                ccRecipient.HasValue ? [ccRecipient.Value.Value] : null),
            ct);
}