using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Persistence;

/// <summary>
/// What <c>SqliteWorkflowDatabase</c> itself guarantees to everything built on it.
///
/// <para>
/// The fixture is shared scaffolding: <c>RetryScannerHarness</c>, <c>StepLeaseOwnershipTests</c>,
/// <c>ScheduledRetryExecutionTests</c> and <c>RequestWorkerStartupRecoveryTests</c> all reach SQLite through
/// it, so a change to how it hands a connection to EF reaches all of them at once. These tests pin the
/// properties those callers depend on: one connection per context, the hand-written schema visible to every
/// context, rows outliving the context that wrote them, and one fixture never seeing another's rows.
/// </para>
///
/// <para>
/// Deliberately no <c>[Trait("Category", "Integration")]</c>: the "Testing" section of
/// <c>docs/architecture/module-conventions.md</c> reserves that trait for tests that need an external SQL
/// Server through <c>IntegrationDb</c>, so a CI filter written as <c>Category != Integration</c> would drop
/// precisely this guard.
/// </para>
/// </summary>
public sealed class SqliteWorkflowDatabaseTests
{
    [Fact(DisplayName = "Every WorkflowDbContext from one fixture gets its own DbConnection instance")]
    public async Task Should_GiveEachContextItsOwnConnection_When_TwoContextsComeFromOneDatabase()
    {
        // T-096: the scanner's poll thread and the test thread each create and dispose commands on the
        // connection their context reports. One shared SqliteConnection instance between them is the defect
        // (IndexOutOfRangeException in SqliteConnection.RemoveCommand), and DbConnection is not thread-safe
        // for any ADO.NET provider, so the guard is the instance identity rather than a timing window.
        await using var db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var a = db.CreateDbContext();
        await using var b = db.CreateDbContext();

        Assert.NotSame(a.Database.GetDbConnection(), b.Database.GetDbConnection());
    }

    [Fact(DisplayName = "The hand-written schema is visible to a context created after the bootstrap")]
    public async Task Should_ExposeBothTables_When_AContextIsCreatedAfterTheSchemaBootstrap()
    {
        await using var db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var ctx = db.CreateDbContext();

        Assert.Empty(await ctx.Requests.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await ctx.RequestSteps.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "A request written through one context is readable through the next")]
    public async Task Should_ReadBackTheAggregate_When_TheWritingContextIsAlreadyDisposed()
    {
        await using var db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var repository = db.CreateRepository();

        var requestId = Guid.CreateVersion7();
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            status: RequestStatus.InProgress,
            currentState: "step-1",
            steps: [RequestAggregateBuilder.BuildStep(requestId: requestId)]);

        await repository.AddAsync(aggregate, TestContext.Current.CancellationToken);

        var reloaded = await repository.FindByIdAsync(requestId, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.Equal(requestId, reloaded.RequestId);
        Assert.Single(reloaded.Steps);
    }

    [Fact(DisplayName = "Two SqliteWorkflowDatabase instances alive at once share no rows")]
    public async Task Should_StayIsolated_When_ASecondDatabaseIsAliveAtTheSameTime()
    {
        await using var first = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var second = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);

        var aggregate = RequestAggregateBuilder.Build();
        await first.CreateRepository().AddAsync(aggregate, TestContext.Current.CancellationToken);

        Assert.NotNull(await first.CreateRepository()
            .FindByIdAsync(aggregate.RequestId, TestContext.Current.CancellationToken));
        Assert.Null(await second.CreateRepository()
            .FindByIdAsync(aggregate.RequestId, TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "A DateTimeOffset ordering comparison still translates to SQL on the fixture")]
    public async Task Should_ReturnOnlyTheDueStep_When_FindDueRetriesAsyncComparesNextRetryAt()
    {
        // OrderableDateTimeOffsetCustomizer is the reason NextRetryAt <= now can run at all on SQLite: the
        // default mapping cannot translate an ordering comparison. The fixture is the only place that
        // converter is installed, so any rework of the fixture has to keep it.
        await using var db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var repository = db.CreateRepository();

        var dueId = await SeedRetryScheduledAsync(repository, TimeSpan.FromMinutes(-1));
        var futureId = await SeedRetryScheduledAsync(repository, TimeSpan.FromMinutes(10));

        var due = await repository.FindDueRetriesAsync(
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Contains(due, entry => entry.Aggregate.RequestId == dueId);
        Assert.DoesNotContain(due, entry => entry.Aggregate.RequestId == futureId);
    }

    [Fact(DisplayName = "A disposed fixture leaves no database behind for the rest of the run")]
    public async Task Should_DropTheInMemoryDatabase_When_TheFixtureIsDisposed()
    {
        // T-096: Microsoft.Data.Sqlite pools connections, and a pooled connection stays open, so every
        // context's connection would keep this fixture's shared-cache in-memory database alive for the
        // whole test run. Measured on this tree: without the pool being cleared the schema below survives.
        var db = await SqliteWorkflowDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var connectionString = db.ConnectionString;
        await db.DisposeAsync();

        // why: Pooling=False keeps this probe out of Microsoft.Data.Sqlite's pool, so it cannot leak the same way the fixture it is testing used to.
        await using var reopened = new SqliteConnection($"{connectionString};Pooling=False");
        await reopened.OpenAsync(TestContext.Current.CancellationToken);

        var command = reopened.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Requests';";
        Assert.Equal(
            0L,
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> SeedRetryScheduledAsync(
        WorkflowDbContextRepository repository,
        TimeSpan dueIn)
    {
        var requestId = Guid.CreateVersion7();

        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            executionStatus: StepExecutionStatus.RetryScheduled,
            stepRetries: 1,
            nextRetryAt: DateTimeOffset.UtcNow.Add(dueIn));

        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            status: RequestStatus.InProgress,
            currentState: step.StepName,
            steps: [step]);

        await repository.AddAsync(aggregate, TestContext.Current.CancellationToken);
        return requestId;
    }
}