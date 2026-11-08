using Core.DataAccess.ResourceRequests;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.ResourceRequest.Flow.Handlers;

/// <summary>
/// First step of the PI-approved path: invalidates the now-used single-use action-link token (ADR-0064 — clears
/// the projection's <c>TokenHash</c>/<c>TokenExpiresAt</c> so it can never verify again), then looks the requested
/// <c>(provider, resource name)</c> up in the catalog and emits whether an assignable resource is already there,
/// so the branch transition can route: <c>exists → AttachResource</c>,
/// <c>missing → NotifyProviderResourceNeeded</c>.
/// </summary>
internal sealed class CheckResourceExistsHandler(
    IResourceRequestProjectionStore store,
    IRepository<ProjectResource, ProjectResourceKey> resources) : IStepHandler<CheckResourceExistsStep>
{
    public async Task<StepResult> HandleAsync(CheckResourceExistsStep request, CancellationToken ct)
    {
        var row = await store.RequireBySlugAsync(request.Slug, ct);

        // The PI decision has settled — the action-link token is spent, invalidate it (single-use, ADR-0064).
        await store.UpdateBySlugAsync(request.Slug, r =>
        {
            r.TokenHash = null;
            r.TokenExpiresAt = null;
        }, ct);

        var key = new ProjectResourceKey(
            ProviderName.Create(row.ProviderName),
            ResourceName.Create(row.ResourceName));

        var resource = await resources.FindAsync(key, ct);

        // Variant B (DEC-3): the requester types a free-text resource name, so "no row at all" is the normal
        // wait-for-the-provider case, not a failure.
        return new CheckResourceExistsResult(resource is not null).ToStepResult();
    }
}