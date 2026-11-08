using Core.Domain.OnboardingRequest;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// How a listed onboarding request relates to the caller: one the caller sent, or one received on a project
/// the caller manages. A read-side view, not a state of the aggregate (ADR-0107).
/// </summary>
public enum OnboardingRequestDirection
{
    Sent,
    Received,
}

/// <summary>
/// A single onboarding request as seen on the read side. Carries the <see cref="Slug"/> so the list
/// endpoints can address the subsequent accept/reject calls; the natural project/requester keys are
/// resolved from the shadow FKs at query time.
/// </summary>
public sealed record OnboardingRequestSummary(
    OnboardingRequestSlug? Slug,
    OnboardingRequestState State,
    ProjectShortName ProjectShortName,
    IdpId IdpUserId,
    Email ContactEmail,
    OnboardingRequestDirection Direction);

/// <summary>
/// The caller-centric list filter (ADR-0107). A null <see cref="Requester"/> drops the sent half and an empty
/// <see cref="ManagedProjects"/> drops the received half. <see cref="Project"/> and <see cref="State"/> narrow
/// whichever halves remain.
/// </summary>
public sealed record OnboardingRequestListFilter(
    IdpId? Requester,
    IReadOnlyList<ProjectShortName> ManagedProjects,
    ProjectShortName? Project,
    int? State);

/// <summary>
/// The settlement context for a single onboarding request, resolved by slug. Because the aggregate
/// <see cref="OnboardingRequest.Project"/> / <see cref="OnboardingRequest.Requester"/> natural keys are
/// <c>Ignore()</c>d in EF (only the <c>ProjectId</c>/<c>UserId</c> shadow FKs are persisted), a loaded
/// aggregate cannot expose them — so accept/reject resolve them here via joins. Carries the
/// <see cref="State"/> for the consumer to reason about (e.g. anti-leak / already-terminal checks).
/// </summary>
public sealed record OnboardingRequestSettlement(
    ProjectShortName Project,
    IdpId Requester,
    OnboardingRequestState State);