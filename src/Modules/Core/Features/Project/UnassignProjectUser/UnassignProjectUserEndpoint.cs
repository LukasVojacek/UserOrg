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

namespace Core.Features.Project.UnassignProjectUser;

/// <summary>
/// Removes a user from a project (membership + project role together). The project owner cannot be
/// removed. Mirrors legacy <c>DELETE api/Project/{ProjectShortName}/User/{IdpUserId}</c>.
/// </summary>
internal static class UnassignProjectUserEndpoint
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
                AuditActions.ProjectUserUnassigned, caller, request.ProjectShortName) with
            {
                TargetType = AuditActions.TargetTypes.User,
                TargetId = request.IdpUserId,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return denied;
        }

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } unassignedBy)
            return Problems.CallerNotProvisioned();

        var target = await users.FindAsync(new IdpId(request.IdpUserId), ct);
        if (target is null)
            return Problems.NotFound("project.user_not_found", "The target user does not exist.");

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
            return Problems.RoleServiceUnavailable();

        var ownerRole = roles.Value.FindByName(roleOptions.Value.DefaultProjectOwnerRoleName);
        if (ownerRole is null)
            return Problems.Upstream(CommonErrorCodes.RoleServiceUnavailable, "Project owner role is not configured.");

        var project = await repository.FindAsync(request.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        var unassign = project.UnassignUserWithRole(target, unassignedBy, ProjectRoleId.Create(ownerRole.Id));
        if (!unassign.IsSuccess)
            return unassign.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [target.IdentityProviderId.Value]);
        await cache.EvictAsync(CacheTags.Project(request.ProjectShortName.Value), ct);
        await cache.EvictAsync(CacheTags.UserInfo(request.IdpUserId), ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectUserUnassigned, caller, request.ProjectShortName) with
        {
            TargetType = AuditActions.TargetTypes.User,
            TargetId = request.IdpUserId,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUnassignProjectUserEndpoint()
        {
            app.MapDelete("Projects/{projectShortName}/Users/{idpUserId}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UnassignProjectUserV2")
            .WithSummary("Remove a user from a project")
            .WithDescription("Removes a user's membership and project role from the project. The project owner cannot be removed (409). Returns 404 when the user is not an active member, 409 when the project is read-only.")
            .WithTags("Project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}