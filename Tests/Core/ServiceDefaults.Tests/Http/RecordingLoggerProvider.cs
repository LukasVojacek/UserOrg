using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace UserOrg.ServiceDefaults.Tests.Http;

internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

    public IEnumerable<string> WarningsMentioning(string text) =>
        Entries
            .Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(text, StringComparison.Ordinal))
            .Select(entry => $"{entry.Category}: {entry.Message}");

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<(string Category, LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, logLevel, formatter(state, exception)));
    }
}