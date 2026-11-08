using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

using Core.DataAccess.Diagnostics;

using Microsoft.Extensions.Logging;

using Moq;

namespace Core.UnitTests.Persistence.Diagnostics;

/// <summary>
/// D2a — a read blocked behind an open writer's lock must fail well inside the old 300 s
/// <c>CommandTimeout</c> window, and the failure must name the query tag, the command text and the configured
/// timeout instead of the opaque hang the incident reported. It must not name a blocking session: by the time
/// this interceptor runs, the failed command has already finished, so <c>sys.dm_exec_requests.blocking_session_id</c>
/// would only ever report <c>0</c> ("not blocked") or match the lookup's own command — never the real blocker
/// (F2). Drives <see cref="BlockedReadDiagnosticsInterceptor"/> directly (rather than through a live SQL
/// Server) with a fake blocked-read exception, so the test proves the diagnostic logic itself rather than any
/// particular server's timing.
/// </summary>
public sealed class BlockedReadDiagnosticsInterceptorTests
{
    [Fact]
    public async Task Should_NameTheQueryAndTimeout_When_AReadTimesOut()
    {
        var classifier = new Mock<IBlockedReadClassifier>();
        classifier.Setup(c => c.IsLockWaitOrCommandTimeout(It.IsAny<Exception>())).Returns(true);

        var logger = new Mock<ILogger<BlockedReadDiagnosticsInterceptor>>();
        var interceptor = new BlockedReadDiagnosticsInterceptor(classifier.Object, logger.Object);

        using var command = new FakeDbCommand
        {
            CommandText = "-- ProviderRepository.FindAsync\r\n-- File.cs:159\r\nSELECT 1",
            CommandTimeout = 30,
        };
        var exception = new TimeoutException("Timeout expired.");

        var stopwatch = Stopwatch.StartNew();
        await interceptor.HandleCommandFailureAsync(command, exception, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        // The diagnostic path itself must never become a second stall — nowhere near the old 300 s window.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"diagnostics took {stopwatch.Elapsed}, expected well under 5s");

        // The query tag, the command text and the configured timeout are logged — and, critically, no
        // blocking-session id is emitted at all: sys.dm_exec_requests.blocking_session_id is a fabricated
        // read by the time this handler runs (F2), so the field is gone rather than lying.
        VerifyLog(logger, LogLevel.Error, Times.Once(), "ProviderRepository.FindAsync", "30", command.CommandText);
        VerifyLogDoesNotContain(logger, "Blocking session");
    }

    [Fact]
    public async Task Should_LogNothing_When_TheFailureIsNotALockWaitOrCommandTimeout()
    {
        var classifier = new Mock<IBlockedReadClassifier>();
        classifier.Setup(c => c.IsLockWaitOrCommandTimeout(It.IsAny<Exception>())).Returns(false);

        var logger = new Mock<ILogger<BlockedReadDiagnosticsInterceptor>>();
        var interceptor = new BlockedReadDiagnosticsInterceptor(classifier.Object, logger.Object);

        using var command = new FakeDbCommand { CommandText = "-- SomeOtherQuery\r\nSELECT 1", CommandTimeout = 30 };

        await interceptor.HandleCommandFailureAsync(command, new InvalidOperationException("unrelated"), TestContext.Current.CancellationToken);

        VerifyLog(logger, LogLevel.Error, Times.Never());
    }

    private static void VerifyLog(Mock<ILogger<BlockedReadDiagnosticsInterceptor>> logger, LogLevel level, Times times, params string[] mustContainAll) =>
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => mustContainAll.Length == 0 || mustContainAll.All(s => state!.ToString()!.Contains(s, StringComparison.Ordinal))),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    /// <summary>Asserts none of the log calls the mock recorded contain <paramref name="mustNotContain"/>.</summary>
    private static void VerifyLogDoesNotContain(Mock<ILogger<BlockedReadDiagnosticsInterceptor>> logger, string mustNotContain) =>
        logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state!.ToString()!.Contains(mustNotContain, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

    /// <summary>
    /// Minimal <see cref="DbCommand"/> double — the interceptor only reads <see cref="CommandText"/> and
    /// <see cref="CommandTimeout"/>; every other member is unreachable from the code under test.
    /// </summary>
    private sealed class FakeDbCommand : DbCommand
    {
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() => throw new NotSupportedException();

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() => throw new NotSupportedException();

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<object> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            _items.Add(value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values) => _items.AddRange(values.Cast<object>());

        public override void Clear() => _items.Clear();

        public override bool Contains(object value) => _items.Contains(value);

        public override bool Contains(string value) => false;

        public override void CopyTo(Array array, int index) => throw new NotSupportedException();

        public override IEnumerator GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(object value) => _items.IndexOf(value);

        public override int IndexOf(string parameterName) => -1;

        public override void Insert(int index, object value) => _items.Insert(index, value);

        public override void Remove(object value) => _items.Remove(value);

        public override void RemoveAt(int index) => _items.RemoveAt(index);

        public override void RemoveAt(string parameterName) => throw new NotSupportedException();

        protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];

        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();

        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
    }
}
