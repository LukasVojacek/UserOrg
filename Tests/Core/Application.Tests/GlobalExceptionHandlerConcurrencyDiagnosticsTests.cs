using System.Diagnostics;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrganizationManagement.API;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Coverage for issue #265 (PUT /api/Requests/{id}/Accept 500 on <see cref="DbUpdateConcurrencyException"/>):
/// the handler must respond 409 and log enough per-entry metadata (entity type, table, key, state) to
/// diagnose which save failed on the next remote occurrence, without leaking property values.
///
/// A real (not mocked) EF InMemory model is used to produce a genuine <see cref="DbUpdateConcurrencyException"/>
/// with populated <c>Entries</c> - <c>IUpdateEntry</c> is EF-internal and cannot be hand-mocked. The InMemory
/// provider only raises this exception when a concurrency token mismatches (it does not replicate SQL Server's
/// affected-row check for plain PK-only saves), so the fixtures below declare their own token; the production
/// model has none (that is the point of the underlying bug), so a throwaway local entity/context is used
/// instead of the production <c>DatabaseContext</c>.
/// </summary>
public sealed class GlobalExceptionHandlerConcurrencyDiagnosticsTests
{
    private const string SecretPropertyValue = "do-not-log-me-secret-value";

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// A hand-rolled logger, not a Moq mock: Moq/Castle DynamicProxy cannot build a proxy for
    /// <c>ILogger&lt;GlobalExceptionHandler&gt;</c> because <see cref="GlobalExceptionHandler"/> is internal
    /// and <c>Microsoft.Extensions.Logging.Abstractions</c> is strong-named (Castle then requires an
    /// InternalsVisibleTo to its own dynamically-signed proxy assembly, which this repo does not grant).
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>
    /// Simulates a broken logging sink (e.g. a full disk, an unreachable log shipper) for Error-level records
    /// only - the concurrency diagnostic - so a test can prove that failure does not turn the already-decided
    /// 409 back into an unhandled 500, without also breaking the unrelated Warning-level HttpResponseClientError log.
    /// </summary>
    private sealed class ThrowingOnErrorLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                throw new InvalidOperationException("Simulated logging sink failure.");
            }
        }
    }

    private sealed class TrackedEntity
    {
        public int Id { get; set; }
        public string Secret { get; set; } = "";
        public string ConcurrencyStamp { get; set; } = "";
    }

    private sealed class CompositeKeyEntity
    {
        public int PartA { get; set; }
        public int PartB { get; set; }
        public string ConcurrencyStamp { get; set; } = "";
    }

    private sealed class ConcurrencyTestContext(DbContextOptions<ConcurrencyTestContext> options) : DbContext(options)
    {
        public DbSet<TrackedEntity> TrackedEntities => Set<TrackedEntity>();
        public DbSet<CompositeKeyEntity> CompositeKeyEntities => Set<CompositeKeyEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrackedEntity>().Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
            modelBuilder.Entity<CompositeKeyEntity>(b =>
            {
                b.HasKey(e => new { e.PartA, e.PartB });
                b.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
            });
        }
    }

    private static DefaultHttpContext BuildHttpContext() => new()
    {
        TraceIdentifier = "trace-abc-123",
        Response = { Body = new MemoryStream() }
    };

    private static async Task<ProblemDetails> ReadProblemDetailsAsync(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        return (await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body))!;
    }

    private static async Task<DbUpdateConcurrencyException> CaptureSingleKeyConflictAsync(string databaseName, int id)
    {
        var options = new DbContextOptionsBuilder<ConcurrencyTestContext>().UseInMemoryDatabase(databaseName).Options;

        using (var seeder = new ConcurrencyTestContext(options))
        {
            seeder.TrackedEntities.Add(new TrackedEntity { Id = id, Secret = SecretPropertyValue, ConcurrencyStamp = "v1" });
            await seeder.SaveChangesAsync();
        }

        using var writer = new ConcurrencyTestContext(options);
        var tracked = await writer.TrackedEntities.FindAsync(id);

        using (var otherRequest = new ConcurrencyTestContext(options))
        {
            var concurrentEntity = await otherRequest.TrackedEntities.FindAsync(id);
            concurrentEntity!.ConcurrencyStamp = "v2-written-by-another-request";
            await otherRequest.SaveChangesAsync();
        }

        tracked!.Secret = "mutated-locally";
        return await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => writer.SaveChangesAsync());
    }

    private static async Task<DbUpdateConcurrencyException> CaptureCompositeKeyConflictAsync(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ConcurrencyTestContext>().UseInMemoryDatabase(databaseName).Options;

        using (var seeder = new ConcurrencyTestContext(options))
        {
            seeder.CompositeKeyEntities.Add(new CompositeKeyEntity { PartA = 1, PartB = 2, ConcurrencyStamp = "v1" });
            await seeder.SaveChangesAsync();
        }

        using var writer = new ConcurrencyTestContext(options);
        var tracked = await writer.CompositeKeyEntities.FindAsync(1, 2);

        using (var otherRequest = new ConcurrencyTestContext(options))
        {
            var concurrentEntity = await otherRequest.CompositeKeyEntities.FindAsync(1, 2);
            concurrentEntity!.ConcurrencyStamp = "v2-written-by-another-request";
            await otherRequest.SaveChangesAsync();
        }

        tracked!.ConcurrencyStamp = "stale-write-attempt";
        return await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => writer.SaveChangesAsync());
    }

    [Fact(DisplayName = "DbUpdateConcurrencyException maps to 409, not 500, with a ProblemDetails body")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_Returns409()
    {
        var exception = await CaptureSingleKeyConflictAsync(nameof(TryHandleAsync_DbUpdateConcurrencyException_Returns409), id: 1);
        var handler = new GlobalExceptionHandler(new RecordingLogger<GlobalExceptionHandler>());
        var httpContext = BuildHttpContext();

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);

        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    [Fact(DisplayName = "DbUpdateConcurrencyException logs the request's correlation id")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_LogsCorrelationIdFromActivity()
    {
        var exception = await CaptureSingleKeyConflictAsync(nameof(TryHandleAsync_DbUpdateConcurrencyException_LogsCorrelationIdFromActivity), id: 7);
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var httpContext = BuildHttpContext();

        using var activity = new Activity("test-activity");
        activity.Start();
        var expectedCorrelationId = activity.TraceId.ToHexString();

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.Contains(logger.Records, e => e.Level == LogLevel.Error && e.Message.Contains(expectedCorrelationId));
    }

    [Fact(DisplayName = "DbUpdateConcurrencyException falls back to TraceIdentifier when there is no current Activity")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_LogsTraceIdentifierWhenNoActivity()
    {
        var previousActivity = Activity.Current;
        Activity.Current = null;
        try
        {
            var exception = await CaptureSingleKeyConflictAsync(nameof(TryHandleAsync_DbUpdateConcurrencyException_LogsTraceIdentifierWhenNoActivity), id: 8);
            var logger = new RecordingLogger<GlobalExceptionHandler>();
            var handler = new GlobalExceptionHandler(logger);
            var httpContext = BuildHttpContext();

            await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

            Assert.Contains(logger.Records, e => e.Level == LogLevel.Error && e.Message.Contains(httpContext.TraceIdentifier));
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    [Fact(DisplayName = "A failure while describing ex.Entries does not turn the 409 back into an unhandled 500")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_DiagnosticLoggingThrows_StillReturns409()
    {
        var exception = await CaptureSingleKeyConflictAsync(nameof(TryHandleAsync_DbUpdateConcurrencyException_DiagnosticLoggingThrows_StillReturns409), id: 9);
        var handler = new GlobalExceptionHandler(new ThrowingOnErrorLogger<GlobalExceptionHandler>());
        var httpContext = BuildHttpContext();

        var result = await Record.ExceptionAsync(() => handler.TryHandleAsync(httpContext, exception, CancellationToken.None).AsTask());

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);

        var problem = await ReadProblemDetailsAsync(httpContext);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact(DisplayName = "DbUpdateConcurrencyException logs entity type and key, never a property value")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_LogsKeyAndTypeButNotPropertyValues()
    {
        const int id = 42;
        var exception = await CaptureSingleKeyConflictAsync(nameof(TryHandleAsync_DbUpdateConcurrencyException_LogsKeyAndTypeButNotPropertyValues), id);
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var httpContext = BuildHttpContext();

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.Contains(logger.Records, e =>
            e.Level == LogLevel.Error
            && e.Message.Contains(nameof(TrackedEntity))
            && e.Message.Contains(id.ToString())
            && !e.Message.Contains(SecretPropertyValue));
    }

    [Fact(DisplayName = "DbUpdateConcurrencyException with a composite key logs every key column")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_CompositeKey_LogsAllKeyColumns()
    {
        var exception = await CaptureCompositeKeyConflictAsync(nameof(TryHandleAsync_DbUpdateConcurrencyException_CompositeKey_LogsAllKeyColumns));
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var httpContext = BuildHttpContext();

        var result = await Record.ExceptionAsync(() => handler.TryHandleAsync(httpContext, exception, CancellationToken.None).AsTask());

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Contains(logger.Records, e =>
            e.Level == LogLevel.Error
            && e.Message.Contains("PartA=1")
            && e.Message.Contains("PartB=2"));
    }

    [Fact(DisplayName = "TryHandleAsync never throws when Entries is empty, and still logs exactly one diagnostic record")]
    public async Task TryHandleAsync_DbUpdateConcurrencyException_EmptyEntries_DoesNotThrowAndLogsOneFallbackRecord()
    {
        var exception = new DbUpdateConcurrencyException("Concurrency conflict with no reported entries");
        var logger = new RecordingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var httpContext = BuildHttpContext();

        var result = await Record.ExceptionAsync(() => handler.TryHandleAsync(httpContext, exception, CancellationToken.None).AsTask());

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        // Exactly one Error-level record: an empty Entries must not silence the diagnostic - a 409 with
        // no matching log line teaches us nothing about the next remote occurrence.
        Assert.Single(logger.Records, e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "Table description falls back to a placeholder when GetTableName() is null")]
    public void DescribeTable_NullTableName_ReturnsPlaceholder()
    {
        var description = ConcurrencyEntryDiagnostics.DescribeTable(null);

        Assert.Equal(ConcurrencyEntryDiagnostics.UnknownTable, description);
    }

    [Fact(DisplayName = "Key description falls back to a placeholder when there is no primary key")]
    public void DescribeKey_NullPrimaryKey_ReturnsPlaceholder()
    {
        var description = ConcurrencyEntryDiagnostics.DescribeKey(null, _ => throw new InvalidOperationException("must not be called"));

        Assert.Equal(ConcurrencyEntryDiagnostics.UnknownKey, description);
    }
}
