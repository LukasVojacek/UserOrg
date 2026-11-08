using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Core.Features.Resource.Workflow;

using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;

using Xunit;

namespace Core.UnitTests.Features.Resource.Workflow;

/// <summary>
/// Unit tests for <see cref="LoggingCallbackAdapter"/> — the platform <see cref="WorkflowEngine.Core.Ports.ICallbackPort"/>
/// MVP (ADR-0056 A4). The adapter logs the terminal <see cref="RequestAggregate"/> (id + flow status);
/// these tests capture the emitted log entry and assert both facts are present.
/// </summary>
public sealed class LoggingCallbackAdapterTests
{
    private static RequestAggregate TerminalAggregate(Guid requestId, RequestStatus status) =>
        RequestAggregate.Rehydrate(
            requestId,
            strategy: "create-resource",
            payload: JsonDocument.Parse("{}"),
            status: status,
            currentState: null,
            clientId: null,
            callbackUrl: null,
            idempotencyKey: null,
            result: null,
            errorCode: null,
            errorMessage: null,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            completedAt: DateTimeOffset.UtcNow,
            rowVersion: null,
            steps: []);

    [Fact]
    public async Task Should_LogRequestIdAndTerminalStatus_When_RequestCompleted()
    {
        var logger = new CapturingLogger<LoggingCallbackAdapter>();
        var adapter = new LoggingCallbackAdapter(logger);
        var requestId = Guid.CreateVersion7();

        await adapter.SendAsync(TerminalAggregate(requestId, RequestStatus.Completed), TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(requestId.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RequestStatus.Completed), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_LogTerminalStatus_When_RequestFailed()
    {
        var logger = new CapturingLogger<LoggingCallbackAdapter>();
        var adapter = new LoggingCallbackAdapter(logger);

        await adapter.SendAsync(TerminalAggregate(Guid.CreateVersion7(), RequestStatus.Failed), TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(nameof(RequestStatus.Failed), entry.Message, StringComparison.Ordinal);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
