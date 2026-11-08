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

namespace Core.Features.Project.CreateProjectTag;

internal static class CreateProjectTagEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        [FromRoute] ProjectShortName ProjectShortName,
        [FromBody] Body Body);

    internal sealed record Body(string Name);

    internal sealed record Response(string Name);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IRepository<ProjectTagAggregate, TagName> tagRepository,
        [FromServices] IUserInfoQuery users,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(request.ProjectShortName) is { } denied)
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectTagCreated, caller, request.ProjectShortName) with
            {
                TargetType = AuditActions.TargetTypes.ProjectTag,
                TargetId = request.Body.Name,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            }, logger);
            return denied;
        }

        if (!Domain.Project.TagName.TryCreate(request.Body.Name, out var tagName, out var error))
            return Problems.Validation(ProjectErrorCodes.TagNameInvalid, error);

        var project = await repository.FindAsync(request.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        // The acting user owns a newly created tag (audit). Resolve their UserName from the trusted sub claim.
        var actor = await users.GetBySubAsync(caller.IdpId, ct);
        if (actor is null)
            return Problems.NotFound(CommonErrorCodes.CallerNotProvisioned, "The calling identity is not provisioned.");

        // Idempotent: skip minting a catalogue tag when the project already has it (mirrors the old
        // aggregate short-circuit; resolve-or-create now lives in the use-case, not the aggregate).
        if (project.HasTag(tagName))
            return Results.Created($"v2/ProjectTags/{tagName.Value}", new Response(tagName.Value));

        // Resolve-or-create behind the repository port: an active tag is returned as-is; otherwise AddAsync's
        // ensure semantics revive a soft-deleted same-name row (or insert a new one) and return the tracked
        // catalogue aggregate — never null on this path (see IRepository<ProjectTag, TagName>.AddAsync).
        var tag = await tagRepository.FindAsync(tagName, ct)
            ?? (await tagRepository.AddAsync(ProjectTagAggregate.Create(tagName, actor.UserName), ct))!;
        if (project.AssignExistingTag(tag) is { IsSuccess: false } assign)
            return assign.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await cache.EvictAsync(CacheTags.Project(request.ProjectShortName.Value), ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectTagCreated, caller, request.ProjectShortName) with
        {
            TargetType = AuditActions.TargetTypes.ProjectTag,
            TargetId = tagName.Value,
        }, logger);

        return Results.Created($"v2/ProjectTags/{tagName.Value}", new Response(tagName.Value));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateProjectTagEndpoint()
        {
            app.MapPost("Projects/{projectShortName}/Tags", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateProjectTagV2")
            .WithSummary("Create a tag and assign it to a project")
            .WithDescription("Creates the named tag in the shared catalogue if it does not yet exist and assigns it to the project. Idempotent. Requires the tag-create permission.")
            .WithTags("Project")
            .Accepts<Body>("application/json")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}