using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using ProjectAggregate = Core.Domain.Project.Project;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Project.UnassignProjectResource;

/// <summary>
/// Removes a resource's assignment from a project (soft-deletes the <c>AssignedProjectResourceToProject</c>
/// link under the Project aggregate, F5). The resource is addressed by its per-provider natural key from the
/// route (ADR-0026/0031). Admin may unassign any provider's resource; a provider service-key identity only its
/// own (<see cref="ProviderWriteAuthorization"/>, ADR-0011). The resource read-only guard applies (parity with
/// the F4 resource-write tightening); there is no non-shared check on remove.
/// </summary>
internal static class UnassignProjectResourceEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        [FromRoute] string ProjectShortName,
        [FromRoute] string ProviderName,
        [FromRoute] string ResourceName);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IRepository<ResourceAggregate, ProjectResourceKey> resources,
        [FromServices] IResourceAssignmentQuery assignmentQuery,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProviderWriteAuthorization.Authorize(caller, request.ProviderName, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectResourceUnassigned, caller, data: new Dictionary<string, object?>
                {
                    ["project"] = request.ProjectShortName,
                    ["provider"] = request.ProviderName,
                }) with
            {
                TargetType = AuditActions.TargetTypes.Resource,
                TargetId = request.ResourceName,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (!ProjectResourceRoute.TryParse(request.ProjectShortName, request.ProviderName, request.ResourceName, out var target, out var invalid))
            return invalid;

        var project = await projects.FindAsync(target.ProjectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        // Load the resource ENTITY by its per-provider natural key (ADR-0044 L2.4). A name under another
        // provider, or a NULL-ProviderId legacy row, never resolves → 404 (invariant #5 at the boundary).
        var resource = await resources.FindAsync(target.ResourceKey, ct);
        if (!ProjectResourceRoute.IsWritable(resource, target.ResourceKey.Name, out var rejected))
            return rejected;

        var result = project.RemoveResource(resource);
        if (!result.IsSuccess)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await ProjectResourceRoute.EvictAsync(cache, assignmentQuery, target, ct);

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectResourceUnassigned, caller, target.ProjectShortName,
            new Dictionary<string, object?> { ["provider"] = target.ResourceKey.Provider.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Resource,
            TargetId = target.ResourceKey.Name.Value,
        }, logger);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUnassignProjectResourceEndpoint()
        {
            app.MapDelete("Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UnassignProjectResourceV2")
            .WithSummary("Remove a resource from a project")
            .WithDescription("Removes a resource's assignment from a project (Admin or the owning provider identity). The resource is addressed by its per-provider natural key in the route (ADR-0026). Returns 204; 400 on input validation; 403 when not allowed; 404 when the project or resource (or a cross-provider name) does not exist, or the resource is not assigned; 422 when the resource is read-only. Use the machine-readable `code`, not the message.")
            .WithTags("Project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}