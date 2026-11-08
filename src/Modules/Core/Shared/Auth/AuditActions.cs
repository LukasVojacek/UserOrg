namespace Core.Shared.Auth;

/// <summary>
/// Catalog of Core's audit action names. Every name is exactly three dot-separated segments,
/// <c>area.entity.verb</c>, with the verb in past tense; a segment that needs two words uses
/// snake_case (<c>resource_request</c>, <c>user_role</c>) because the dots separate the three
/// roles, not words. These strings are persisted forever in the append-only audit log, where a
/// later rename orphans every historical event under the old spelling and forces every query over
/// history to know both — so the scheme is fixed now and this list is append-only too.
/// </summary>
internal static class AuditActions
{
    internal const string ProjectUserRoleChanged = "project.user_role.changed";
    internal const string ResourceRequestActionSettled = "resource_request.action.settled";

    internal const string ProjectUserAssigned = "project.user.assigned";
    internal const string ProjectUserUnassigned = "project.user.unassigned";
    internal const string ProjectSelfCreated = "project.self.created";
    internal const string ProjectSelfUpdated = "project.self.updated";
    internal const string ProjectSelfDeleted = "project.self.deleted";
    internal const string ProjectTagCreated = "project.tag.created";
    internal const string ProjectTagAssigned = "project.tag.assigned";
    internal const string ProjectTagUnassigned = "project.tag.unassigned";
    internal const string ProjectTagSelfDeleted = "project_tag.self.deleted";
    internal const string ProjectResourceAssigned = "project.resource.assigned";
    internal const string ProjectResourceUnassigned = "project.resource.unassigned";
    internal const string UserProviderAdminGranted = "user.provider_admin.granted";
    internal const string UserProviderAdminRevoked = "user.provider_admin.revoked";
    internal const string UserPreferencesUpdated = "user.preferences.updated";
    internal const string UserSelfProvisioned = "user.self.provisioned";
    internal const string OnboardingRequestSelfAccepted = "onboarding_request.self.accepted";
    internal const string OnboardingRequestSelfRejected = "onboarding_request.self.rejected";
    internal const string RequestSelfCreated = "request.self.created";
    internal const string RequestSelfAccepted = "request.self.accepted";
    internal const string RequestSelfRejected = "request.self.rejected";
    internal const string RequestActionLinkSettled = "request.action_link.settled";
    internal const string RequestActionLinkViewed = "request.action_link.viewed";
    internal const string ResourceRequestSelfCreated = "resource_request.self.created";
    internal const string ResourceRequestSelfCancelled = "resource_request.self.cancelled";
    internal const string ResourceRequestApprovalLinkReissued = "resource_request.approval_link.reissued";
    internal const string ResourceRequestActionViewed = "resource_request.action.viewed";
    internal const string ProviderSelfCreated = "provider.self.created";
    internal const string ProviderSelfUpdated = "provider.self.updated";
    internal const string ProviderSelfDeactivated = "provider.self.deactivated";
    internal const string ProviderSelfReactivated = "provider.self.reactivated";
    internal const string ProviderLocationAdded = "provider.location.added";
    internal const string ProviderLocationUpdated = "provider.location.updated";
    internal const string ProviderLocationRemoved = "provider.location.removed";
    internal const string ProviderResourceAssignmentCreated = "provider.resource_assignment.created";
    internal const string ProviderResourceAssignmentUpdated = "provider.resource_assignment.updated";
    internal const string ProviderResourceAssignmentRemoved = "provider.resource_assignment.removed";
    internal const string ResourceCreationRequested = "resource.creation.requested";
    internal const string ResourceSelfUpdated = "resource.self.updated";
    internal const string ResourceSelfDeleted = "resource.self.deleted";
    internal const string ResourceSelfPublished = "resource.self.published";
    internal const string CacheOutputFlushed = "cache.output.flushed";

    /// <summary>
    /// Audit reasons that have no domain <c>Error</c> to borrow a code from. Like every reason that
    /// reaches <c>AuditEvent.Reason</c> they are stable area-qualified codes and never prose, so the
    /// append-only log stays greppable; they are declared here rather than at the call site so the
    /// reason vocabulary has one visible source instead of a private one per handler.
    /// </summary>
    internal static class DenialReasons
    {
        /// <summary>
        /// The caller has no scope over the project. The handler deliberately answers with the masked
        /// project-not-found problem, which would record the cover story instead of the cause.
        /// </summary>
        internal const string ProjectCallerOutOfScope = "project.caller_out_of_scope";
        internal const string ProjectNotFound = "project.not_found";
        internal const string ResourceRequestNotAwaitingDecision = "resource_request.not_awaiting_decision";
        internal const string UserRoleWriteForbidden = "user.role_write_forbidden";
        internal const string ProviderWriteForbidden = "provider.write_forbidden";
        internal const string ProjectTagWriteForbidden = "project_tag.write_forbidden";
        internal const string CacheResetForbidden = "cache.reset_forbidden";
    }

    internal static class TargetTypes
    {
        internal const string User = "user";
        internal const string Project = "project";
        internal const string ProjectTag = "project_tag";
        internal const string Provider = "provider";
        internal const string Location = "location";
        internal const string ResourceAssignment = "resource_assignment";
        internal const string Resource = "resource";
        internal const string Request = "request";
        internal const string ResourceRequest = "resource_request";
        internal const string OnboardingRequest = "onboarding_request";
    }
}