using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Logging;

namespace Core.UnitTests.Shared;

public sealed class AuditLoggerExtensionsTests
{
    private const string Action = "project.tag.created";
    private const string DataValue = "data-value-that-must-not-leak";

    private sealed class Category;

    private sealed class CapturingLogger : ILogger
    {
        internal sealed record Entry(
            LogLevel Level,
            string Message,
            Exception? Exception,
            IReadOnlyList<KeyValuePair<string, object?>> State);

        internal List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(
                logLevel,
                formatter(state, exception),
                exception,
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
    }

    private static AuditEvent DeniedEvent() => AuditEvent.Denied(Action, "actor-1", "user") with
    {
        TargetType = "project_tag",
        TargetId = "tag-1",
        Reason = "project_tag.write_forbidden",
        Data = new Dictionary<string, object?> { ["tag"] = DataValue },
    };

    [Fact]
    public async Task Should_ForwardTheEventUnchanged_When_TheSinkAccepts()
    {
        var sink = new RecordingAuditLogger<Category>();
        var logger = new CapturingLogger();
        var auditEvent = DeniedEvent();

        await sink.AuditBestEffortAsync(auditEvent, logger);

        Assert.Same(auditEvent, Assert.Single(sink.Events));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Should_NotPropagate_When_TheSinkThrows()
    {
        var sink = new RecordingAuditLogger<Category> { ThrowOn = AuditOutcome.Denied };

        var exception = await Record.ExceptionAsync(async () =>
            await sink.AuditBestEffortAsync(DeniedEvent(), new CapturingLogger()));

        Assert.Null(exception);
        Assert.Single(sink.Events);
    }

    [Fact]
    public async Task Should_LogOneWarningWithActionOutcomeAndExceptionType_When_TheSinkThrows()
    {
        var sink = new RecordingAuditLogger<Category> { ThrowOn = AuditOutcome.Denied };
        var logger = new CapturingLogger();

        await sink.AuditBestEffortAsync(DeniedEvent(), logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(Action, entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AuditOutcome.Denied), entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_KeepTheExceptionMessageAndTheEventDataOutOfTheWarning_When_TheSinkThrows()
    {
        var sink = new RecordingAuditLogger<Category> { ThrowOn = AuditOutcome.Denied };
        var logger = new CapturingLogger();

        await sink.AuditBestEffortAsync(DeniedEvent(), logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(RecordingAuditLogger<Category>.SinkFailureMessage, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(DataValue, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(entry.State, kv =>
            kv.Value?.ToString() is { } value
            && (value.Contains(DataValue, StringComparison.Ordinal)
                || value.Contains(RecordingAuditLogger<Category>.SinkFailureMessage, StringComparison.Ordinal)));
    }
}
