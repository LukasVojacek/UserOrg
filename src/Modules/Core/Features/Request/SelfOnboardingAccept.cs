using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.Features.Request;

/// <summary>
/// Outcome of accepting a self-onboarding request. On failure <see cref="Failure"/> carries the HTTP
/// problem; on success it is null and the requester details are set for notification + cache eviction.
/// </summary>
internal sealed record SelfOnboardingAcceptOutcome(
    IResult? Failure,
    Email? RequesterEmail,
    IdpId RequesterIdpId);

/// <summary>
/// Shared accept logic for a pending self-onboarding request: transitions the request to Accepted and
/// grants the requester project membership via the Slice D <c>AssignUserWithRole</c> write path.
/// Mutates the tracked aggregates only — the caller owns <c>SaveChangesAsync</c>, notifications and cache.
/// Used by both the authenticated accept endpoint and the anonymous action-link handle endpoint.
/// </summary>
internal static class SelfOnboardingAccept
{
    internal static async Task<SelfOnboardingAcceptOutcome> ExecuteAsync(
        RequestAggregate request,
        ProjectShortName shortName,
        UserId assignedBy,
        IRepository<ProjectAggregate, ProjectShortName> projects,
        IRepository<Domain.User.User, IdpId> users,
        IUserDirectory userDirectory,
        IRoleListService roleListService,
        WellKnownRoles roleOptions,
        CancellationToken ct)
    {
        var requesterIdpId = await userDirectory.FindIdpIdAsync(request.UserId, ct);
        if (requesterIdpId is null)
            return new(Problems.NotFound(RequestErrorCodes.NotFound, "Request not found."), null, default);

        var requester = await users.FindAsync(requesterIdpId.Value, ct);
        if (requester is null)
            return new(Problems.NotFound(RequestErrorCodes.NotFound, "Request not found."), null, default);

        var project = await projects.FindAsync(shortName, ct);
        if (project is null)
            return new(Problems.NotFound(RequestErrorCodes.NotFound, "Request not found."), null, default);

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

        return new(null, requester.Email, requesterIdpId.Value);
    }
}