namespace Core.Features.Request.Notifications;

/// <summary>
/// Outbound notification port for the self-onboarding request flow. Invoked inline by the use-cases
/// after <c>SaveChangesAsync</c> (ADR-0009) — not from domain-event handlers, because action-link
/// generation and the emails that surround it are an endpoint-level step.
/// </summary>
internal interface INotificationSender
{
    Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct);

    Task SendRequestCreatedToOwnerAsync(Email ownerEmail, Guid requestId, ProjectShortName project, CancellationToken ct);

    Task SendRequestAcceptedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct);

    Task SendRequestRejectedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct);

    /// <summary>
    /// Send a notification to requester that a new resource has been created and provisioned for them. This is a new notification for the resource creation flow, which is separate from the self-onboarding request flow.
    /// </summary>
    /// <param name="requesterEmail"></param>
    /// <param name="requestId"></param>
    /// <param name="resourceName"></param>
    /// <param name="actionToken"></param>
    /// <param name="project"></param>
    /// <param name="resourceName"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, string actionToken, ResourceName resourceName, CancellationToken ct);

    /// <summary>
    /// Emails the PI (person responsible for the resource) a single-use action link asking them to
    /// approve/reject a project↔resource assignment request (ADR-0064). The link is
    /// <c>{ResourceRequestActionLink:BaseUrl}/{actionToken}</c>; the token is minted and stored (hashed) by
    /// the flow. Mirrors legacy <c>NewProjectAssignment_PM</c>.
    /// </summary>
    Task SendPiApprovalRequestedAsync(Email piEmail, string requestSlug, string actionToken, ProjectShortName project, ResourceName resource, CancellationToken ct);

    /// <summary>
    /// Notifies the requester that their resource was assigned to the project. Mirrors legacy
    /// <c>RequestState</c> with an <c>Assigned</c> state.
    /// </summary>
    Task SendRequesterAssignedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, CancellationToken ct);

    /// <summary>
    /// Notifies the requester that the PI rejected the assignment request, optionally carrying the PI's
    /// reason. Mirrors legacy <c>RequestState</c> with a <c>Rejected</c> state.
    /// </summary>
    Task SendRequesterRejectedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, string? reason, CancellationToken ct);

    /// <summary>
    /// Notifies the provider's support address that an approved request awaits creation of the requested
    /// resource, CCing the requester's contact address. Mirrors legacy <c>NewProjectAssignmentHPC</c>.
    /// </summary>
    Task SendProviderResourceNeededAsync(Email providerSupportEmail, string requestSlug, ProjectShortName project, ResourceName resource, Email requesterContact, CancellationToken ct);

    /// <summary>
    /// Sends an email to the recipient notifying them that a new account has been created for the specified resource. This is part of the self-onboarding request flow.
    /// </summary>
    /// <param name="recipientEmail"></param>
    /// <param name="resourceName"></param>
    /// <param name="clusters"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task SendRequestToCreateAccountAsync(Email recipientEmail, string resourceName, List<(string, string, string, string?)> clusters, CancellationToken ct);

    /// <summary>
    /// Sends an email to the recipient notifying them that the specified resource has been published and is now available for use. This is part of the self-onboarding request flow.
    /// </summary>
    /// <param name="recipientEmail"></param>
    /// <param name="resourceName"></param>
    /// <param name="projectShortName"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task SendResourcePublishedAsync(Email recipientEmail, string resourceName, string projectShortname, CancellationToken ct);
}