using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

/// <summary>
/// Write port for the ProjectResource aggregate (ADR-0026 / F4 / ADR-0044 L2.4). Keyed by the natural
/// <see cref="ProjectResourceKey"/> <c>(ProviderName, ResourceName)</c> — the surrogate <c>ProjectResourceId</c>
/// stays a storage detail and never crosses a contract. <see cref="FindAsync"/> resolves the provider name to
/// its owning provider internally and loads the resource by <c>(ProviderId, Name)</c>, including the
/// <c>_provider</c> navigation so the natural provider identity (<c>ProviderName</c>) is whole after
/// materialisation (ADR-0031).
/// </summary>
internal sealed partial class DatabaseContext : IRepository<Domain.Provider.ProjectResource, ProjectResourceKey>
{
    async ValueTask<Domain.Provider.ProjectResource?> IRepository<Domain.Provider.ProjectResource, ProjectResourceKey>.AddAsync(Domain.Provider.ProjectResource aggregate, CancellationToken cancellationToken)
    {
        // ProjectResource.Id is database-generated (NewId()); do not assign it here.
        await ProjectResources.AddAsync(aggregate, cancellationToken);
        RegisterAddedAggregateRoot(aggregate);
        return aggregate;
    }

    void IRepository<Domain.Provider.ProjectResource, ProjectResourceKey>.Delete(Domain.Provider.ProjectResource aggregate, CancellationToken cancellationToken) => ProjectResources.Remove(aggregate);

    async ValueTask<Domain.Provider.ProjectResource?> IRepository<Domain.Provider.ProjectResource, ProjectResourceKey>.FindAsync(ProjectResourceKey key, CancellationToken cancellationToken)
    {
        // Resolve the owning provider's surrogate from its natural key (ADR-0029). The Provider query filter
        // excludes deactivated providers, so a missing/deactivated provider yields no row → null.
        var providerId = await Providers
            .Where(p => p.Name == key.Provider)
            .Select(p => EF.Property<ProviderId>(p, "Id"))
            .Cast<ProviderId?>()
            .FirstOrDefaultAsync(cancellationToken);

        if (providerId is null)
        {
            return null;
        }

        // The active resource carrying this name under that provider. The !IsDeleted query filter applies,
        // so a soft-deleted row resolves to null exactly like a missing one. _provider is included so the
        // materialised aggregate exposes its natural key (ADR-0031).
        return await ProjectResources
            .Include("_provider")
            .FirstOrDefaultAsync(
                pr => pr.ResourceName == key.Name && EF.Property<ProviderId?>(pr, "ProviderId") == providerId,
                cancellationToken);
    }
}