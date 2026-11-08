using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Read-query tests over a seeded in-memory <see cref="ResourceRequestsDbContext"/> (ADR-0063 projection).
/// Covers the four IDataQuery-style lookups: paged list-by-project(+state), find-by-slug,
/// find-by-valid-token-hash and find-awaiting-by-resource-key.
/// </summary>
public sealed class ProjectResourceRequestQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private sealed class InMemoryFactory(string storeName) : IDbContextFactory<ResourceRequestsDbContext>
    {
        public ResourceRequestsDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ResourceRequestsDbContext>()
                .UseInMemoryDatabase(storeName)
                .Options);
    }

    private static async Task<IProjectResourceRequestQuery> SeedAsync(CancellationToken ct)
    {
        var factory = new InMemoryFactory(Guid.NewGuid().ToString());
        await using (var db = factory.CreateDbContext())
        {
            db.ProjectRequests.AddRange(
                Row("proj-gpu", "proj", "acme", "gpu", ResourceRequestState.PendingPiApproval, tokenHash: "valid", tokenExpiresAt: Now.AddHours(1), updatedAt: Now.AddDays(-31)),
                Row("proj-gpu-2", "proj", "acme", "gpu-old", ResourceRequestState.Assigned, tokenHash: null, tokenExpiresAt: null, updatedAt: Now.AddDays(-31)),
                Row("proj-fpga", "proj", "acme", "fpga", ResourceRequestState.AwaitingResourceCreation, tokenHash: "expired", tokenExpiresAt: Now.AddHours(-1), updatedAt: Now.AddDays(-1)),
                Row("other-gpu", "other", "acme", "fpga", ResourceRequestState.AwaitingResourceCreation, tokenHash: null, tokenExpiresAt: null, updatedAt: Now));
            await db.SaveChangesAsync(ct);
        }

        return new ProjectResourceRequestQuery(factory);
    }

    private static ProjectResourceRequest Row(
        string slug, string project, string provider, string resource,
        ResourceRequestState state, string? tokenHash, DateTimeOffset? tokenExpiresAt, DateTimeOffset? updatedAt = null) => new()
        {
            Slug = slug,
            ProjectShortName = project,
            ProviderName = provider,
            ResourceName = resource,
            RequesterIdpId = "idp",
            ContactEmail = "pi@example.org",
            State = state,
            TokenHash = tokenHash,
            TokenExpiresAt = tokenExpiresAt,
            WorkflowRequestId = Guid.CreateVersion7(),
            Reason = null,
            CreatedAt = Now,
            UpdatedAt = updatedAt ?? Now
        };

    [Fact]
    public async Task Should_ReturnProjectRowsPaged_When_ListedByProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        var page = await query.ListByProjectAsync("proj", state: null, page: 1, limit: 10, ct);

        Assert.Equal(3, page.TotalItems);
        Assert.All(page.Items, i => Assert.Equal("proj", i.ProjectShortName));
    }

    [Fact]
    public async Task Should_FilterByState_When_StateProvidedToList()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        var page = await query.ListByProjectAsync("proj", "PendingPiApproval", page: 1, limit: 10, ct);

        Assert.Equal(1, page.TotalItems);
        Assert.Equal("proj-gpu", page.Items[0].Slug);
        Assert.Equal("PendingPiApproval", page.Items[0].State);
    }

    [Fact]
    public async Task Should_ReturnDetail_When_FoundBySlug()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        var detail = await query.FindBySlugAsync("proj-gpu", ct);

        Assert.NotNull(detail);
        Assert.Equal("pi@example.org", detail.ContactEmail);
        Assert.Null(await query.FindBySlugAsync("missing", ct));
    }

    [Fact]
    public async Task Should_ReturnRow_When_TokenHashValidAndUnexpired()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        var hit = await query.FindByValidTokenHashAsync("valid", Now, ct);
        Assert.NotNull(hit);
        Assert.Equal("proj-gpu", hit.Slug);
    }

    [Fact]
    public async Task Should_ReturnNull_When_TokenHashExpired()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        Assert.Null(await query.FindByValidTokenHashAsync("expired", Now, ct));
        Assert.Null(await query.FindByValidTokenHashAsync("unknown", Now, ct));
    }

    [Fact]
    public async Task Should_ReturnAwaitingRowsAcrossProjects_When_FoundByResourceKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        var rows = await query.FindAwaitingByResourceKeyAsync("acme", "fpga", ct);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Slug == "proj-fpga");
        Assert.Contains(rows, r => r.Slug == "other-gpu");
    }

    [Fact]
    public async Task Should_ReturnOnlyNonTerminalRowsPastCutoff_When_FindingDueForExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        // Cutoff sits between the two non-terminal rows' UpdatedAt: proj-gpu (31 days stale) is past it,
        // proj-fpga (1 day stale) and the terminal proj-gpu-2 (also 31 days stale, but Assigned) are not returned.
        var rows = await query.FindDueForExpiryAsync(Now.AddDays(-30), ct);

        Assert.Equal(["proj-gpu"], rows.Select(r => r.Slug));
    }
}
