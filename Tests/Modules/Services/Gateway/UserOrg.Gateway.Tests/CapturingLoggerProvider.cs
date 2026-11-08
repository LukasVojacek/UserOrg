using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace UserOrg.Gateway.Tests;

internal sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, EventId EventId);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> All => [.. _entries];

    public IReadOnlyList<CapturedLogEntry> From<TCategory>() =>
        [.. _entries.Where(entry => entry.Category == typeof(TCategory).FullName)];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception), eventId));
    }
}