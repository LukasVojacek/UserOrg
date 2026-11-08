using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Persistence;

namespace WorkflowEngine.Core.Tests.Persistence;

/// <summary>
/// Lease semantics of <see cref="WorkflowDbContextRepository.TryAcquireStepLeaseAsync"/> against a real
/// relational provider. EF InMemory cannot run <c>ExecuteUpdateAsync</c> and the test fake always granted
/// the lease, which is why the first-execution path below went unnoticed. The contended and expired-lease
/// paths reach that ExecuteUpdate, which SQLite cannot translate — they need SQL Server to cover.
/// </summary>
public sealed class StepLeaseTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private PooledFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new PooledFactory(options);

        await using var ctx = _factory.CreateDbContext();
        await CreateSchemaAsync(ctx);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Should_Acquire_Lease_When_Step_Row_Does_Not_Exist_Yet()
    {
        // The worker leases a step BEFORE RecordStepStarted creates its row, so the first execution of
        // any step has nothing to update. Reporting "already leased" there strands the request forever.
        var repo = new WorkflowDbContextRepository(_factory);
        var aggregate = await SeedRequestAsync();

        var acquired = await repo.TryAcquireStepLeaseAsync(
            aggregate.RequestId, "SendPiApprovalEmail", "worker-1", TimeSpan.FromMinutes(5));

        Assert.True(acquired);
    }

    // EnsureCreated cannot build this model on SQLite — the mapping carries SQL Server column types
    // (nvarchar(max), rowversion). The tables are declared here instead, mirroring InitialWorkflow;
    // SQLite has no schemas, so the provider drops the "workflow" prefix.
    private static async Task CreateSchemaAsync(WorkflowDbContext ctx) =>
        await ctx.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE Requests (
                RequestId TEXT NOT NULL CONSTRAINT PK_Requests PRIMARY KEY,
                ClientId TEXT NULL, Strategy TEXT NOT NULL, Status TEXT NOT NULL,
                CurrentState TEXT NULL, Payload TEXT NOT NULL, CallbackUrl TEXT NULL,
                IdempotencyKey TEXT NULL, Result TEXT NULL, ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                CompletedAt TEXT NULL, RowVersion BLOB NULL);

            CREATE TABLE RequestSteps (
                StepId TEXT NOT NULL CONSTRAINT PK_RequestSteps PRIMARY KEY,
                RequestId TEXT NOT NULL, StepName TEXT NOT NULL, ExecutionStatus TEXT NOT NULL,
                MessageType TEXT NOT NULL, InputData TEXT NULL, StepRetries INTEGER NOT NULL DEFAULT 0,
                NextRetryAt TEXT NULL, LockedBy TEXT NULL, LockedUntil TEXT NULL,
                StartedAt TEXT NULL, CompletedAt TEXT NULL, Result TEXT NULL,
                ErrorCode TEXT NULL, ErrorMessage TEXT NULL);
            """);

    private async Task<RequestAggregate> SeedRequestAsync()
    {
        var repo = new WorkflowDbContextRepository(_factory);
        var aggregate = RequestAggregate.Create(
            "assign-resource-to-project",
            JsonDocument.Parse("""{"Slug":"p-r"}"""),
            clientId: null,
            callbackUrl: null,
            idempotencyKey: null,
            now: DateTimeOffset.UtcNow);

        await repo.AddAsync(aggregate);
        return aggregate;
    }

    private sealed class PooledFactory(DbContextOptions<WorkflowDbContext> options)
        : IDbContextFactory<WorkflowDbContext>
    {
        public WorkflowDbContext CreateDbContext() => new(options);
    }
}
