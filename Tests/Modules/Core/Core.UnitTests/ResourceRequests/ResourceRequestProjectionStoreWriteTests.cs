using Core.DataAccess.ResourceRequests;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Write-port tests for the create endpoint's row lifecycle (B05): the initial insert and the flow-start-failure
/// cleanup delete over the schema-isolated projection store.
/// </summary>
public sealed class ResourceRequestProjectionStoreWriteTests
{
    private sealed class InMemoryFactory(string storeName) : IDbContextFactory<ResourceRequestsDbContext>
    {
        public ResourceRequestsDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ResourceRequestsDbContext>()
                .UseInMemoryDatabase(storeName)
                .Options);
    }

    private static ProjectResourceRequest NewRow(string slug) => new()
    {
        Slug = slug,
        ProjectShortName = "proj",
        ProviderName = "acme",
        ResourceName = "gpu",
        RequesterIdpId = "idp-1",
        ContactEmail = "pi@example.org",
        State = ResourceRequestState.PendingPiApproval,
        WorkflowRequestId = Guid.Empty,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Should_PersistRow_When_Inserted()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new InMemoryFactory(Guid.NewGuid().ToString());
        var store = new ResourceRequestProjectionStore(factory);

        await store.InsertAsync(NewRow("proj-gpu"), ct);

        Assert.NotNull(await store.GetBySlugAsync("proj-gpu", ct));
    }

    [Fact]
    public async Task Should_RemoveRow_When_DeletedBySlug()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new InMemoryFactory(Guid.NewGuid().ToString());
        var store = new ResourceRequestProjectionStore(factory);
        await store.InsertAsync(NewRow("proj-gpu"), ct);

        await store.DeleteBySlugAsync("proj-gpu", ct);

        Assert.Null(await store.GetBySlugAsync("proj-gpu", ct));
    }

    [Fact]
    public async Task Should_NotThrow_When_DeletingMissingSlug()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new InMemoryFactory(Guid.NewGuid().ToString());
        var store = new ResourceRequestProjectionStore(factory);

        await store.DeleteBySlugAsync("missing", ct);

        Assert.Null(await store.GetBySlugAsync("missing", ct));
    }

    [Fact]
    public async Task Should_ThrowRowMissing_When_UpdatingMissingSlug()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new InMemoryFactory(Guid.NewGuid().ToString());
        var store = new ResourceRequestProjectionStore(factory);

        // Distinct from the generic InvalidOperationException SQL Server itself throws for e.g. connection-pool
        // exhaustion, so callers (AttachResourceHandler) can classify this delete-race case as permanent without
        // misclassifying an unrelated transient failure that happens to share that base type.
        await Assert.ThrowsAsync<ResourceRequestRowMissingException>(
            () => store.UpdateBySlugAsync("missing", r => r.State = ResourceRequestState.Assigned, ct));
    }
}
