using Core.DataAccess.Outbox;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Round-trip tests for <see cref="OutboxDbContext"/> over the EF Core in-memory provider. The in-memory
/// provider cannot apply the real migration (the schema-isolated <c>outbox.Outbox</c> table + history are
/// verified against a real DB by the developer/CI per ADR-0041); these tests assert the entity model maps
/// and every column round-trips through a save/read against the same store.
/// </summary>
public sealed class OutboxDbContextTests
{
    private static OutboxDbContext NewContext(string storeName) =>
        new(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseInMemoryDatabase(storeName)
            .Options);

    [Fact]
    public async Task Should_RoundTripAllColumns_When_OutboxMessagePersistedAndRead()
    {
        var storeName = Guid.NewGuid().ToString();
        var ct = TestContext.Current.CancellationToken;

        var id = Guid.CreateVersion7();
        var occurredAt = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var dispatchedAt = new DateTimeOffset(2026, 6, 29, 10, 0, 5, TimeSpan.Zero);
        var lastAttemptAt = new DateTimeOffset(2026, 6, 29, 10, 0, 4, TimeSpan.Zero);

        await using (var writeContext = NewContext(storeName))
        {
            writeContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                Type = "Core.Domain.Project.ProjectCreatedEvent",
                Payload = """{"projectShortName":"abc"}""",
                OccurredAt = occurredAt,
                DispatchedAt = dispatchedAt,
                Attempts = 2,
                LastAttemptAt = lastAttemptAt,
                Error = "transient failure"
            });
            await writeContext.SaveChangesAsync(ct);
        }

        await using var readContext = NewContext(storeName);
        var read = await readContext.OutboxMessages.SingleAsync(x => x.Id == id, ct);

        Assert.Equal(id, read.Id);
        Assert.Equal("Core.Domain.Project.ProjectCreatedEvent", read.Type);
        Assert.Equal("""{"projectShortName":"abc"}""", read.Payload);
        Assert.Equal(occurredAt, read.OccurredAt);
        Assert.Equal(dispatchedAt, read.DispatchedAt);
        Assert.Equal(2, read.Attempts);
        Assert.Equal(lastAttemptAt, read.LastAttemptAt);
        Assert.Equal("transient failure", read.Error);
    }

    [Fact]
    public async Task Should_LeaveNullableColumnsNull_When_UndispatchedMessagePersisted()
    {
        var storeName = Guid.NewGuid().ToString();
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.CreateVersion7();

        await using (var writeContext = NewContext(storeName))
        {
            writeContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                Type = "Core.Domain.User.UserCreatedEvent",
                Payload = "{}",
                OccurredAt = DateTimeOffset.UtcNow
            });
            await writeContext.SaveChangesAsync(ct);
        }

        await using var readContext = NewContext(storeName);
        var read = await readContext.OutboxMessages.SingleAsync(x => x.Id == id, ct);

        Assert.Null(read.DispatchedAt);
        Assert.Null(read.LastAttemptAt);
        Assert.Null(read.Error);
        Assert.Equal(0, read.Attempts);
    }
}
