using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using ProjectTagAggregate = Core.Domain.Project.ProjectTag;

namespace Core.Features.ProjectTag.DeleteProjectTag;

/// <summary>
/// Deletes a shared catalogue tag (Admin only). Soft-deletes the entry only when no active project still
/// references it (block-if-in-use, F1 G2); otherwise 409. Diverges from legacy: there is no rename/description
/// (v2 tags are name-only) and delete is blocked while in use. No cache eviction — the tag listing is uncached.
/// </summary>
internal static class DeleteProjectTagEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string tagName,
        Caller caller,
        [FromServices] IRepository<ProjectTagAggregate, TagName> repository,
        [FromServices] IProjectTagQuery tags,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!Domain.Project.TagName.TryCreate(tagName, out var name, out var error))
            return Problems.Validation(ProjectErrorCodes.TagNameInvalid, error);

        // A shared catalogue entry affects every project, so deleting it is platform maintenance, not a
        // project member's right — Admin only (mirrors CreateProvider).
        if (!caller.IsAdmin(roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(AuditActions.ProjectTagSelfDeleted, caller) with
            {
                TargetType = AuditActions.TargetTypes.ProjectTag,
                TargetId = name.Value,
                Reason = AuditActions.DenialReasons.ProjectTagWriteForbidden,
            }, logger);
            return Problems.Forbidden("project_tag.write_forbidden", "Only an administrator may delete a project tag.");
        }

        // FindAsync excludes soft-deleted entries, so a missing or already-deleted tag both read as 404.
        var tag = await repository.FindAsync(name, ct);
        if (tag is null)
            return Problems.NotFound(ProjectTagErrorCodes.TagNotFound, $"Tag '{name.Value}' was not found.");

        if (await tags.IsTagAssignedToAnyProjectAsync(name, ct))
            return Problems.Conflict(ProjectTagErrorCodes.TagInUse, $"Tag '{name.Value}' is assigned to one or more projects and cannot be deleted.");

        if (tag.Delete() is { IsSuccess: false } result)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(AuditActions.ProjectTagSelfDeleted, caller) with
        {
            TargetType = AuditActions.TargetTypes.ProjectTag,
            TargetId = name.Value,
        }, logger);
        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapDeleteProjectTagEndpoint()
        {
            app.MapDelete("ProjectTags/{tagName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("DeleteProjectTagV2")
            .WithSummary("Delete a project tag")
            .WithDescription("Soft-deletes a shared catalogue tag (Admin only). Returns 204; 400 on an invalid name; 403 for non-admins; 404 when the tag does not exist; 409 when the tag is still assigned to an active project.")
            .WithTags("ProjectTag")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}