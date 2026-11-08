using Core.DataAccess.ResourceRequests;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

using ProjectAggregate = Core.Domain.Project.Project;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// Attaches the resource to the project through the F5 write path (<see cref="ProjectAggregate.AssignResource"/>
/// + <see cref="IUnitOfWork.SaveChangesAsync"/>) — no duplicate assignment logic; HEAppE sync rides the existing
/// durable event. Idempotent: an already-active link (<c>ResourceAlreadyAssigned</c>) is treated as success so a
/// retried/replayed step still lands the projection on <c>Assigned</c>. Missing project or resource is permanent.
/// </summary>
internal sealed class AttachResourceHandler(
    IResourceRequestProjectionStore store,
    IRepository<ProjectAggregate, ProjectShortName> projects,
    IRepository<ResourceAggregate, ProjectResourceKey> resources,
    IUnitOfWork unitOfWork) : IStepHandler<AttachResourceStep>
{
    public async Task<StepResult> HandleAsync(AttachResourceStep request, CancellationToken ct)
    {
        var row = await store.RequireBySlugAsync(request.Slug, ct);

        var project = await projects.FindAsync(ProjectShortName.Create(row.ProjectShortName), ct)
            ?? throw new PermanentException("resource_request.project_missing", $"Project '{row.ProjectShortName}' not found.");

        var key = new ProjectResourceKey(
            ProviderName.Create(row.ProviderName),
            ResourceName.Create(row.ResourceName));

        var resource = await resources.FindAsync(key, ct)
            ?? throw new PermanentException("resource_request.resource_missing", $"Resource '{row.ResourceName}' not found.");

        var result = project.AssignResource(resource);
        if (result.IsSuccess)
        {
            await FlowHandlerHelpers.SendOrRetryAsync(
                "resource_request.assignment_save_failed",
                $"Failed to save the resource assignment for '{request.Slug}'.",
                () => unitOfWork.SaveChangesAsync(ct));
        }
        else if (result.Error.Code != ProjectErrorCodes.ResourceAlreadyAssigned)
        {
            throw new PermanentException("resource_request.attach_failed", result.Error.Description);
        }

        // This handler writes to two DbContexts (the Core DatabaseContext above, then the projection's own
        // ResourceRequestsDbContext here) with no transaction spanning them (D4 — a transaction was deliberately
        // not carried over from the resource-assignment path's fix). A transient failure of either write must
        // surface as retryable so the idempotent retry (ResourceAlreadyAssigned above) converges.
        // See docs/architecture/adr/0087-transacted-resource-accept-and-conditional-link-burn.md.
        // ResourceRequestsDbContext already retries transient faults at the EF/statement level
        // (EnableRetryOnFailure); this is a second, step-level retry on top — deliberate, not a duplicate to "fix".
        await FlowHandlerHelpers.SendOrRetryAsync(
            "resource_request.projection_update_failed",
            $"Failed to mark projection row '{request.Slug}' as Assigned.",
            () => store.UpdateBySlugAsync(request.Slug, r => r.State = ResourceRequestState.Assigned, ct));

        return new StepResult(null);
    }
}