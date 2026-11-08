using Core.DataAccess.ResourceRequests;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Round-trip tests for <see cref="ResourceRequestsDbContext"/> over the EF Core in-memory provider. The
/// schema-isolated <c>resourceRequests</c> table, its unique slug key and the filtered single-active unique
/// index (ADR-0066) are verified against a real SQL Server by CI/developer per the HEAppE/Outbox precedent
/// (ADR-0041); these tests assert the model maps and every column round-trips, including the
/// <see cref="ResourceRequestState"/> string conversion.
/// </summary>
public sealed class ResourceRequestsDbContextTests
{
    private static ResourceRequestsDbContext NewContext(string storeName) =>
        new(new DbContextOptionsBuilder<ResourceRequestsDbContext>()
            .UseInMemoryDatabase(storeName)
            .Options);

    [Fact]
    public async Task Should_RoundTripAllColumns_When_RequestPersistedAndRead()
    {
        var storeName = Guid.NewGuid().ToString();
        var ct = TestContext.Current.CancellationToken;

        var workflowRequestId = Guid.CreateVersion7();
        var createdAt = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var expiresAt = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

        await using (var write = NewContext(storeName))
        {
            write.ProjectRequests.Add(new ProjectResourceRequest
            {
                Slug = "proj-gpu",
                ProjectShortName = "proj",
                ProviderName = "acme",
                ResourceName = "gpu",
                RequesterIdpId = "idp-123",
                ContactEmail = "pi@example.org",
                State = ResourceRequestState.PendingPiApproval,
                TokenHash = "hash",
                TokenExpiresAt = expiresAt,
                WorkflowRequestId = workflowRequestId,
                Reason = null,
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            });
            await write.SaveChangesAsync(ct);
        }

        await using var read = NewContext(storeName);
        var row = await read.ProjectRequests.SingleAsync(r => r.Slug == "proj-gpu", ct);

        Assert.Equal("proj", row.ProjectShortName);
        Assert.Equal("acme", row.ProviderName);
        Assert.Equal("gpu", row.ResourceName);
        Assert.Equal("idp-123", row.RequesterIdpId);
        Assert.Equal("pi@example.org", row.ContactEmail);
        Assert.Equal(ResourceRequestState.PendingPiApproval, row.State);
        Assert.Equal("hash", row.TokenHash);
        Assert.Equal(expiresAt, row.TokenExpiresAt);
        Assert.Equal(workflowRequestId, row.WorkflowRequestId);
        Assert.Null(row.Reason);
        Assert.Equal(createdAt, row.CreatedAt);
    }
}
