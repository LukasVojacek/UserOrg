using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

using ProjectAggregate = Core.Domain.Project.Project;
using ProjectTagAggregate = Core.Domain.Project.ProjectTag;

namespace Core.Features.Project.AssignProjectTag;

internal static class AssignProjectTagEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        [FromRoute] ProjectShortName ProjectShortName,
        [FromRoute] string TagName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IRepository<ProjectTagAggregate, TagName> tagRepository,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(request.ProjectShortName) is { } denied)
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectTagAssigned, caller, request.ProjectShortName) with
            {
                TargetType = AuditActions.TargetTypes.ProjectTag,
                TargetId = request.TagName,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            }, logger);
            return denied;
        }

        if (!Domain.Project.TagName.TryCreate(request.TagName, out var tagName, out var error))
            return Problems.Validation(ProjectErrorCodes.TagNameInvalid, error);

        var project = await repository.FindAsync(request.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        // Assign existing only — never create. A name with no catalogue tag is a 404 (use POST to create).
        // FindAsync filters !IsDeleted, so a soft-deleted tag also reads as missing → 404.
        var tag = await tagRepository.FindAsync(tagName, ct);
        if (tag is null)
            return Problems.NotFound("project.tag_not_found", "No tag with that name exists.");

        if (project.AssignExistingTag(tag) is { IsSuccess: false } assign)
            return assign.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await cache.EvictAsync(CacheTags.Project(request.ProjectShortName.Value), ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectTagAssigned, caller, request.ProjectShortName) with
        {
            TargetType = AuditActions.TargetTypes.ProjectTag,
            TargetId = tagName.Value,
        }, logger);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapAssignProjectTagEndpoint()
        {
            app.MapPut("Projects/{projectShortName}/Tags/{tagName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("AssignProjectTagV2")
            .WithSummary("Assign an existing tag to a project")
            .WithDescription("Associates an existing catalogue tag with the project. Returns 404 when no tag with that name exists; it never creates one (use POST to create). Idempotent. Tags are identified by name.")
            .WithTags("Project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}