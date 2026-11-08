using Core.DataAccess.Queries.Contracts;
using Core.Domain.Primitives;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Read-side queries over the resource-request projection (ADR-0063). Runs against the schema-isolated
/// <see cref="ResourceRequestsDbContext"/> via its pooled factory. Filtering, ordering and paging are pushed
/// to the store in fluent method-syntax (repo convention); the final row-to-DTO mapping is done in memory so
/// the <see cref="ResourceRequestState"/> value-converter is never projected inside the SQL expression.
/// </summary>
internal sealed class ProjectResourceRequestQuery(
    IDbContextFactory<ResourceRequestsDbContext> factory) : IProjectResourceRequestQuery
{
    public async Task<QueryPage<ResourceRequestSummary>> ListByProjectAsync(
        string projectShortName, string? state, int page, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var query = db.ProjectRequests
            .AsNoTracking()
            .Where(r => r.ProjectShortName == projectShortName);

        if (state is not null)
        {
            var parsed = ResourceRequestState.From(state);
            query = query.Where(r => r.State == parsed);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Slug)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        var items = rows
            .Select(r => new ResourceRequestSummary(
                r.Slug,
                r.State.Value,
                r.ProjectShortName,
                r.ProviderName,
                r.ResourceName,
                r.RequesterIdpId,
                r.CreatedAt,
                r.UpdatedAt))
            .ToList();

        return new QueryPage<ResourceRequestSummary>(items, total);
    }

    public async Task<ResourceRequestDetail?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.ProjectRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Slug == slug, ct);

        return row is null ? null : ToDetail(row);
    }

    public async Task<ResourceRequestDetail?> FindByValidTokenHashAsync(string tokenHash, DateTimeOffset asOf, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.ProjectRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash && r.TokenExpiresAt != null && r.TokenExpiresAt > asOf, ct);

        return row is null ? null : ToDetail(row);
    }

    public async Task<IReadOnlyList<ResourceRequestDetail>> FindAwaitingByResourceKeyAsync(
        string providerName, string resourceName, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var awaiting = ResourceRequestState.AwaitingResourceCreation;
        var rows = await db.ProjectRequests
            .AsNoTracking()
            .Where(r => r.ProviderName == providerName
                && r.ResourceName == resourceName
                && r.State == awaiting)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Slug)
            .ToListAsync(ct);

        return rows.Select(ToDetail).ToList();
    }

    public async Task<ResourceRequestDetail?> FindByResourceKeyAsync(string providerName, string resourceName, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.ProjectRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProviderName == providerName && r.ResourceName == resourceName, ct);

        return row is null ? null : ToDetail(row);
    }

    public async Task<IReadOnlyList<string>> ListSlugsByBaseAsync(string baseSlug, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(baseSlug);
        await using var db = await factory.CreateDbContextAsync(ct);

        // Greedy candidate set (bare base + anything under "{base}-"); the pure NextAvailable picks the first free
        // slug from it. Over-fetching here is safe — availability, not a derived ordinal, decides the new slug.
        var suffixPrefix = $"{baseSlug}-";
        return await db.ProjectRequests
            .AsNoTracking()
            .Where(r => r.Slug == baseSlug || r.Slug.StartsWith(suffixPrefix))
            .Select(r => r.Slug)
            .ToListAsync(ct);
    }

    public async Task<bool> HasActiveRequestAsync(
        string projectShortName, string providerName, string resourceName, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var pending = ResourceRequestState.PendingPiApproval;
        var awaiting = ResourceRequestState.AwaitingResourceCreation;
        return await db.ProjectRequests
            .AsNoTracking()
            .AnyAsync(r => r.ProjectShortName == projectShortName
                && r.ProviderName == providerName
                && r.ResourceName == resourceName
                && (r.State == pending || r.State == awaiting), ct);
    }

    public async Task<IReadOnlyList<ResourceRequestDetail>> FindDueForExpiryAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var pending = ResourceRequestState.PendingPiApproval;
        var awaiting = ResourceRequestState.AwaitingResourceCreation;
        var rows = await db.ProjectRequests
            .AsNoTracking()
            .Where(r => (r.State == pending || r.State == awaiting) && r.UpdatedAt <= cutoff)
            .OrderBy(r => r.UpdatedAt)
            .ThenBy(r => r.Slug)
            .ToListAsync(ct);

        return rows.Select(ToDetail).ToList();
    }

    private static ResourceRequestDetail ToDetail(ProjectResourceRequest r) => new(
        r.Slug,
        r.State.Value,
        r.ProjectShortName,
        r.ProviderName,
        r.ResourceName,
        r.RequesterIdpId,
        r.ContactEmail,
        r.WorkflowRequestId,
        r.Reason,
        r.TokenExpiresAt,
        r.CreatedAt,
        r.UpdatedAt);
}