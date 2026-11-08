using Core.Shared.OutputCache;

using Forge.Core.Features.Role.Services;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.Http;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.Project.AssignProjectUser;

/// <summary>
/// Assigns a user to a project as a project member (membership + project role, coordinated by the
/// aggregate). Mirrors legacy <c>PUT api/Project/{ProjectShortName}/User/{IdpUserId}</c>, which always
/// grants the Member role. Role-hierarchy gating for <i>who may call</i> is enforced by the Gateway.
/// </summary>
internal static class AssignProjectUserEndpoint
{
    internal sealed record Request(
        [FromRoute] ProjectShortName ProjectShortName,
        [FromRoute] string IdpUserId);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IRoleListService roleListService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(request.ProjectShortName) is { } denied)
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectUserAssigned, caller, request.ProjectShortName) with
            {
                TargetType = AuditActions.TargetTypes.User,
                TargetId = request.IdpUserId,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return denied;
        }

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } assignedBy)
            return Problems.CallerNotProvisioned();

        var target = await users.FindAsync(new IdpId(request.IdpUserId), ct);
        if (target is null)
            return Problems.NotFound("project.user_not_found", "The target user does not exist.");

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
            return Problems.RoleServiceUnavailable();

        var memberRole = roles.Value.FindByName(roleOptions.Value.DefaultProjectMemberRoleName);
        if (memberRole is null)
            return Problems.Upstream(CommonErrorCodes.RoleServiceUnavailable, "Project member role is not configured.");

        var project = await repository.FindAsync(request.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        var assign = project.AssignUserWithRole(target, assignedBy, ProjectRoleId.Create(memberRole.Id));
        if (!assign.IsSuccess)
            return assign.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [target.IdentityProviderId.Value]);
        await cache.EvictAsync(CacheTags.Project(request.ProjectShortName.Value), ct);
        await cache.EvictAsync(CacheTags.UserInfo(request.IdpUserId), ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectUserAssigned, caller, request.ProjectShortName,
            new Dictionary<string, object?> { ["role_id"] = memberRole.Id }) with
        {
            TargetType = AuditActions.TargetTypes.User,
            TargetId = request.IdpUserId,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapAssignProjectUserEndpoint()
        {
            app.MapPut("Projects/{projectShortName}/Users/{idpUserId}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("AssignProjectUserV2")
            .WithSummary("Assign a user to a project")
            .WithDescription("Assigns a user to the project as a project member (creates or reactivates the membership and the project role). Idempotent reactivation of a previously removed user. Returns 409 when the user is already an active member, or when the project is read-only.")
            .WithTags("Project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}