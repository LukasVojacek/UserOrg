using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// EF write adapter for the resource-request projection (ADR-0063), running against the schema-isolated
/// <see cref="ResourceRequestsDbContext"/> via its pooled factory — mirrors <see cref="ProjectResourceRequestQuery"/>
/// on the read side. Each call opens a short-lived context so it composes with the WorkflowEngine worker's
/// per-step scope without sharing a tracker.
/// </summary>
internal sealed class ResourceRequestProjectionStore(
    IDbContextFactory<ResourceRequestsDbContext> factory) : IResourceRequestProjectionStore
{
    public async Task<ProjectResourceRequest?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ProjectRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
    }

    public async Task InsertAsync(ProjectResourceRequest row, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);

        await using var db = await factory.CreateDbContextAsync(ct);
        db.ProjectRequests.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ResourceRequestConstraintClassifier.Classify(ex) is { } conflict)
        {
            // Translate a uniqueness race into a typed signal the create endpoint maps to a 409 / retry, instead
            // of letting it surface as an opaque 500 (ADR-0066).
            throw new ResourceRequestConflictException(conflict, ex);
        }
    }

    public async Task DeleteBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ProjectRequests.FirstOrDefaultAsync(r => r.Slug == slug, ct);
        if (row is null)
        {
            return;
        }

        db.ProjectRequests.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateBySlugAsync(string slug, Action<ProjectResourceRequest> mutate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ProjectRequests.FirstOrDefaultAsync(r => r.Slug == slug, ct)
            ?? throw new ResourceRequestRowMissingException(slug);

        mutate(row);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// DI helper for the resource-request projection write port. Kept separate from B02's
/// <c>AddResourceRequestsPersistence</c> (which registers the read query + context factory) so B04 wires its own
/// piece; both are invoked from the shared composition by B09.
/// </summary>
public static class ResourceRequestProjectionStoreExtensions
{
    /// <summary>Registers <see cref="IResourceRequestProjectionStore"/>. Requires the context factory from B02's persistence unit.</summary>
    public static IServiceCollection AddResourceRequestProjectionStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IResourceRequestProjectionStore, ResourceRequestProjectionStore>();
        return services;
    }
}