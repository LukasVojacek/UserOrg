using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using WorkflowEngine.Core.Persistence;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// A throwaway SQLite database wired to the real <see cref="WorkflowDbContext"/> model, so tests can drive
/// <see cref="WorkflowDbContextRepository"/> itself instead of a fake.
///
/// This matters for the lease: <c>TryAcquireStepLeaseAsync</c> decides through an
/// <c>ExecuteUpdateAsync</c>, which EF InMemory cannot run at all and which the test fake used to grant
/// unconditionally — that is how the first-execution bug behind ADR-0067 stayed hidden. Only a relational
/// provider can fail here.
///
/// Two accommodations are needed for SQLite:
/// <list type="bullet">
///   <item>the schema is declared by hand — the model carries SQL Server column types (<c>nvarchar(max)</c>,
///   <c>rowversion</c>) that <c>EnsureCreated</c> cannot build, and SQLite has no schemas so the
///   <c>workflow</c> prefix is dropped;</item>
///   <item><see cref="DateTimeOffset"/> is re-mapped through <see cref="DateTimeOffsetToBinaryConverter"/>.
///   SQLite cannot translate an ordering comparison on its default mapping, so <c>LockedUntil &lt; now</c>
///   and <c>NextRetryAt &lt;= now</c> would throw instead of running. The converter is order-preserving, so
///   the comparisons behave as they do on SQL Server.</item>
/// </list>
/// </summary>
internal sealed class SqliteWorkflowDatabase : IDbContextFactory<WorkflowDbContext>, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    private SqliteWorkflowDatabase(SqliteConnection connection, IDbContextFactory<WorkflowDbContext> factory)
    {
        _connection = connection;
        _connectionString = connection.ConnectionString;
        Factory = factory;
    }

    /// <summary>The connection string of this fixture's uniquely named in-memory database.</summary>
    internal string ConnectionString => _connectionString;

    internal IDbContextFactory<WorkflowDbContext> Factory { get; }

    internal WorkflowDbContextRepository CreateRepository() => new(Factory);

    // T-005-05 and T-005-06 grew this helper in parallel worktrees and reached the same SQLite
    // accommodations by two different shapes: one exposing Factory, the other being the factory.
    // Both are kept — the tests of one block register the helper itself as the factory, the tests
    // of the other reach through Factory — so neither block's tests needed editing to merge.
    public WorkflowDbContext CreateDbContext() => Factory.CreateDbContext();

    public Task<WorkflowDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Factory.CreateDbContextAsync(cancellationToken);

    internal static async Task<SqliteWorkflowDatabase> CreateAsync(CancellationToken ct = default)
    {
        // A DbConnection is thread-safe for no ADO.NET provider, so every context must open its own:
        // UseSqlite takes the connection *string*, never the instance. The database is named and
        // shared-cache so those connections all see one schema, and the keep-alive connection below
        // holds it: a shared in-memory database is dropped when its last connection closes.
        var name = $"t{Guid.NewGuid():N}";
        var connectionString = $"Data Source=file:{name}?mode=memory&cache=shared";

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlite(connectionString)
            .ReplaceService<IModelCustomizer, OrderableDateTimeOffsetCustomizer>()
            .Options;

        var factory = new PooledFactory(options);

        await using (var ctx = factory.CreateDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(Schema, ct);
        }

        return new SqliteWorkflowDatabase(connection, factory);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();

        // Disposing a connection only returns it to Microsoft.Data.Sqlite's pool, which keeps it open;
        // measured at EF Core Sqlite 10.0.11, a shared-cache in-memory database therefore outlives its
        // last visible connection and leaks for the rest of the run. Clearing the pool is what actually
        // closes the fixture's connections, including the ones each WorkflowDbContext opened.
        using var pooled = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(pooled);
    }

    // Mirrors the InitialWorkflow migration. The datetimeoffset columns are INTEGER because of the
    // order-preserving converter below.
    private const string Schema =
        """
        CREATE TABLE Requests (
            RequestId TEXT NOT NULL CONSTRAINT PK_Requests PRIMARY KEY,
            ClientId TEXT NULL, Strategy TEXT NOT NULL, Status TEXT NOT NULL,
            CurrentState TEXT NULL, Payload TEXT NOT NULL, CallbackUrl TEXT NULL,
            IdempotencyKey TEXT NULL, Result TEXT NULL, ErrorCode TEXT NULL,
            ErrorMessage TEXT NULL, CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL,
            CompletedAt INTEGER NULL, RowVersion BLOB NULL);

        CREATE TABLE RequestSteps (
            StepId TEXT NOT NULL CONSTRAINT PK_RequestSteps PRIMARY KEY,
            RequestId TEXT NOT NULL, StepName TEXT NOT NULL, ExecutionStatus TEXT NOT NULL,
            MessageType TEXT NOT NULL, InputData TEXT NULL, StepRetries INTEGER NOT NULL DEFAULT 0,
            NextRetryAt INTEGER NULL, LockedBy TEXT NULL, LockedUntil INTEGER NULL,
            StartedAt INTEGER NULL, CompletedAt INTEGER NULL, Result TEXT NULL,
            ErrorCode TEXT NULL, ErrorMessage TEXT NULL);
        """;

    private sealed class PooledFactory(DbContextOptions<WorkflowDbContext> options)
        : IDbContextFactory<WorkflowDbContext>
    {
        public WorkflowDbContext CreateDbContext() => new(options);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by EF Core through ReplaceService<IModelCustomizer, ...>.")]
    private sealed class OrderableDateTimeOffsetCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            ArgumentNullException.ThrowIfNull(modelBuilder);

            var converter = new DateTimeOffsetToBinaryConverter();

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(converter);
                    property.SetColumnType(null);
                }
            }
        }
    }
}
