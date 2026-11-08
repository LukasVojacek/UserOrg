using System.Reflection;

using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins the gateway's fail-open audit write (D10, D17). The helper is resolved by name so that, until it
/// exists, these tests fail on a named assertion instead of breaking the build of every other gateway test.
/// </summary>
public sealed class GatewayAuditExtensionsTests
{
    private const string HelperTypeName = "UserOrg.Gateway.GatewayAuditExtensions";

    private const string HelperMethodName = "AuditBestEffortAsync";

    private const string SinkFailureMessage = "sink-failure-message-4711";

    private const string DataValue = "data-value-0815";

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> State, Exception? Exception)
    {
        public string Text => string.Join(' ', State.Select(pair => pair.Value?.ToString()).Prepend(Message));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                exception));
    }

    private static AuditEvent EventWithOutcome(AuditOutcome outcome)
    {
        var auditEvent = outcome switch
        {
            AuditOutcome.Success => AuditEvent.Success("gateway.rbac.seeded", "gateway", "service"),
            AuditOutcome.Failure => AuditEvent.Failure("gateway.rbac.seeded", "gateway", "service"),
            _ => AuditEvent.Denied("gateway.rbac.seeded", "gateway", "service"),
        };
        return auditEvent with { Data = new Dictionary<string, object?> { ["roles"] = DataValue } };
    }

    private static Task AuditBestEffortAsync(IAuditLogger<GatewayAuditExtensionsTests> auditLogger, AuditEvent auditEvent, ILogger logger)
    {
        var helperType = typeof(GatewayAuditActions).Assembly.GetType(HelperTypeName);
        Assert.True(helperType is not null, $"UserOrg.Gateway declares no {HelperTypeName}");

        var method = helperType.GetMethod(HelperMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(method is { IsGenericMethodDefinition: true }, $"{HelperTypeName} declares no generic {HelperMethodName}<TCategory>");

        return (Task)method.MakeGenericMethod(typeof(GatewayAuditExtensionsTests)).Invoke(null, [auditLogger, auditEvent, logger])!;
    }

    [Fact]
    public async Task Should_ForwardTheEventUnchangedAndLogNothing_When_TheWriteSucceeds()
    {
        var auditEvent = EventWithOutcome(AuditOutcome.Success);
        var auditLogger = new Mock<IAuditLogger<GatewayAuditExtensionsTests>>();
        auditLogger.Setup(l => l.AuditAsync(It.IsAny<AuditEvent>())).Returns(ValueTask.CompletedTask);
        var logger = new RecordingLogger();

        await AuditBestEffortAsync(auditLogger.Object, auditEvent, logger);

        auditLogger.Verify(l => l.AuditAsync(It.Is<AuditEvent>(e => ReferenceEquals(e, auditEvent))), Times.Once);
        auditLogger.VerifyNoOtherCalls();
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(AuditOutcome.Success)]
    [InlineData(AuditOutcome.Failure)]
    [InlineData(AuditOutcome.Denied)]
    public async Task Should_SwallowTheFailureAndLogOneWarningWithoutTheMessageOrData_When_TheWriteThrows(AuditOutcome outcome)
    {
        var auditEvent = EventWithOutcome(outcome);
        var auditLogger = new Mock<IAuditLogger<GatewayAuditExtensionsTests>>();
        auditLogger.Setup(l => l.AuditAsync(It.IsAny<AuditEvent>())).ThrowsAsync(new InvalidOperationException(SinkFailureMessage));
        var logger = new RecordingLogger();

        await AuditBestEffortAsync(auditLogger.Object, auditEvent, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains("gateway.rbac.seeded", entry.Text, StringComparison.Ordinal);
        Assert.Contains(outcome.ToString(), entry.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(SinkFailureMessage, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(DataValue, entry.Text, StringComparison.Ordinal);
    }
}
