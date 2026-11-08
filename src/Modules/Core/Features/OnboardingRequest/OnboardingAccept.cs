using Core.Domain.OnboardingRequest;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.OnboardingRequest;

/// <summary>
/// Outcome of accepting an onboarding request. On failure <see cref="Failure"/> carries the HTTP problem;
/// on success it is null and the requester details are set for notification + cache eviction.
/// </summary>
internal sealed record OnboardingAcceptOutcome(
    IResult? Failure,
    Email? RequesterEmail,
    IdpId RequesterIdpId);

/// <summary>
/// Shared accept logic for a pending onboarding request: transitions the request to Accepted and grants
/// the requester project membership via the <c>AssignUserWithRole</c> write path. Mutates the tracked
/// aggregate only — the caller owns <c>SaveChangesAsync</c>, notifications and cache (ADR-0019: the request
/// transition and the membership grant land in one transaction). The onboarding twin of
/// <c>SelfOnboardingAccept.ExecuteAsync</c>.
/// </summary>
/// <remarks>
/// The aggregate's natural keys (<c>Project</c>/<c>Requester</c>) are EF-ignored shadow FKs, so the loaded
/// aggregate cannot expose the requester. The caller resolves the requester <see cref="IdpId"/> via the
/// settlement query and passes it in; we hydrate the requester <c>User</c> aggregate from it here.
/// </remarks>
internal static class OnboardingAccept
{
    internal static async Task<OnboardingAcceptOutcome> ExecuteAsync(
        OnboardingRequestAggregate request,
        IdpId requesterIdpId,
        ProjectShortName shortName,
        UserId assignedBy,
        IRepository<ProjectAggregate, ProjectShortName> projects,
        IRepository<Domain.User.User, IdpId> users,
        IRoleListService roleListService,
        WellKnownRoles roleOptions,
        CancellationToken ct)
    {
        var requester = await users.FindAsync(requesterIdpId, ct);
        if (requester is null)
            return new(Problems.NotFound(OnboardingRequestErrorCodes.NotFound, "Request not found."), null, default);

        var project = await projects.FindAsync(shortName, ct);
        if (project is null)
            return new(Problems.NotFound(OnboardingRequestErrorCodes.NotFound, "Request not found."), null, default);

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
            return new(Problems.RoleServiceUnavailable(), null, default);

        var memberRole = roles.Value.FindByName(roleOptions.DefaultProjectMemberRoleName);
        if (memberRole is null)
            return new(Problems.Upstream(CommonErrorCodes.RoleServiceUnavailable, "Project member role is not configured."), null, default);

        var accept = request.Accept();
        if (!accept.IsSuccess)
            return new(accept.Error.ToProblem(), null, default);

        var assign = project.AssignUserWithRole(requester, assignedBy, ProjectRoleId.Create(memberRole.Id));
        if (!assign.IsSuccess)
            return new(assign.Error.ToProblem(), null, default);

        return new(null, requester.Email, requesterIdpId);
    }
}