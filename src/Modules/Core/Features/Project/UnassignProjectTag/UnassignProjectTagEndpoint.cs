using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.Project.UnassignProjectTag;

internal static class UnassignProjectTagEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        [FromRoute] ProjectShortName ProjectShortName,
        [FromRoute] string TagName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(request.ProjectShortName) is { } denied)
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectTagUnassigned, caller, request.ProjectShortName) with
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

        if (project.RemoveTag(tagName) is { IsSuccess: false } remove)
            return remove.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await cache.EvictAsync(CacheTags.Project(request.ProjectShortName.Value), ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectTagUnassigned, caller, request.ProjectShortName) with
        {
            TargetType = AuditActions.TargetTypes.ProjectTag,
            TargetId = tagName.Value,
        }, logger);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUnassignProjectTagEndpoint()
        {
            app.MapDelete("Projects/{projectShortName}/Tags/{tagName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UnassignProjectTagV2")
            .WithSummary("Remove a tag from a project")
            .WithDescription("Removes the named tag's association with the project. Idempotent. The shared tag itself is not deleted.")
            .WithTags("Project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}