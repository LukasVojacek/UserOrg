using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Read-query tests for the create-endpoint's two pre-checks (B05): the base-slug collision count that feeds
/// <see cref="ResourceRequestSlug.Create"/> and the single-active guard over the 3-part key.
/// </summary>
public sealed class ResourceRequestCreateQueryTests
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
                Row("proj-gpu", "proj", "acme", "gpu", ResourceRequestState.PendingPiApproval),
                Row("proj-gpu-2", "proj", "globex", "gpu", ResourceRequestState.Assigned),
                Row("proj-fpga", "proj", "acme", "fpga", ResourceRequestState.AwaitingResourceCreation));
            await db.SaveChangesAsync(ct);
        }

        return new ProjectResourceRequestQuery(factory);
    }

    private static ProjectResourceRequest Row(
        string slug, string project, string provider, string resource, ResourceRequestState state) => new()
        {
            Slug = slug,
            ProjectShortName = project,
            ProviderName = provider,
            ResourceName = resource,
            RequesterIdpId = "idp",
            ContactEmail = "pi@example.org",
            State = state,
            WorkflowRequestId = Guid.CreateVersion7(),
            CreatedAt = Now,
            UpdatedAt = Now
        };

    [Fact]
    public async Task Should_ReturnBareAndPrefixedSlugs_When_ListedByBase()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        var gpu = await query.ListSlugsByBaseAsync("proj-gpu", ct);
        Assert.Equal(["proj-gpu", "proj-gpu-2"], gpu.OrderBy(s => s));

        Assert.Equal(["proj-fpga"], await query.ListSlugsByBaseAsync("proj-fpga", ct));
        Assert.Empty(await query.ListSlugsByBaseAsync("proj-none", ct));
    }

    [Fact]
    public async Task Should_ReportActive_When_NonTerminalRowExistsForKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        Assert.True(await query.HasActiveRequestAsync("proj", "acme", "gpu", ct));
        Assert.True(await query.HasActiveRequestAsync("proj", "acme", "fpga", ct));
    }

    [Fact]
    public async Task Should_NotReportActive_When_OnlyTerminalRowOrNoneForKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = await SeedAsync(ct);

        // proj/globex/gpu exists but is Assigned (terminal).
        Assert.False(await query.HasActiveRequestAsync("proj", "globex", "gpu", ct));
        Assert.False(await query.HasActiveRequestAsync("proj", "acme", "none", ct));
    }
}
