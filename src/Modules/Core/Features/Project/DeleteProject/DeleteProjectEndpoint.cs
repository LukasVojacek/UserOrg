using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.Http;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.Project.DeleteProject;

/// <summary>
/// Soft-deletes a project and cascade soft-deletes every project role row (<c>UserPermission</c>) on it.
/// Mirrors legacy <c>DELETE api/Project/{ProjectShortName}</c>.
/// </summary>
internal static class DeleteProjectEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] ProjectShortName projectShortName,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(projectShortName) is { } denied)
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectSelfDeleted, caller, projectShortName) with
            {
                TargetType = AuditActions.TargetTypes.Project,
                TargetId = projectShortName.Value,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return denied;
        }

        var project = await repository.FindAsync(projectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        // Snapshot affected members before Delete() soft-deletes their project-role rows.
        var affectedUsers = project.ActiveMemberIdpIds;

        if (project.Delete() is { IsSuccess: false } delete)
            return delete.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, affectedUsers.Select(idpId => idpId.Value));

        await cache.EvictAsync(CacheTags.Project(projectShortName.Value), ct);
        foreach (var idpId in affectedUsers)
            await cache.EvictAsync(CacheTags.UserInfo(idpId.Value), ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectSelfDeleted, caller, projectShortName,
            new Dictionary<string, object?> { ["affected_member_count"] = affectedUsers.Count }) with
        {
            TargetType = AuditActions.TargetTypes.Project,
            TargetId = projectShortName.Value,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapDeleteProjectEndpoint()
        {
            app.MapDelete("Projects/{projectShortName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("DeleteProjectV2")
            .WithSummary("Delete a project")
            .WithDescription("Soft-deletes the project and cascade soft-deletes all project role rows. Returns 204; 404 out of scope / not found; 409 when the project is read-only.")
            .WithTags("Project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}