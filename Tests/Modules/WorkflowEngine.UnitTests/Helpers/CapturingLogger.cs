using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>One captured log entry: its level, its rendered message and the exception attached to it.</summary>
internal readonly record struct CapturedLog(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// A minimal <see cref="ILogger{T}"/> that records every entry (all levels enabled), so a test can assert
/// on <em>how many times</em> a component logged something, not only that it logged at all.
/// <para>
/// The count is the point for the retry scanner: a component that logs the same error once per poll for
/// the lifetime of the process is a log flood, and the only way to tell that apart from a single
/// diagnostic is to count. Registered last in the container so it wins over the
/// <c>ILogger&lt;T&gt;</c> the logging factory would otherwise provide.
/// </para>
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<CapturedLog> _entries = [];

    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>
    /// How many <see cref="LogLevel.Warning"/>-or-worse entries mention <paramref name="fragment"/> in their
    /// rendered message — the request id, typically, so entries about another request never inflate the count.
    /// <para>
    /// Warning and not Error is the threshold on purpose: the retry scanner reports one shape of an
    /// unusable message at <c>Error</c> and another at <c>Warning</c>, and both flood the log equally.
    /// </para>
    /// </summary>
    public int CountFailureLogsMentioning(string fragment) => Entries
        .Count(entry => entry.Level >= LogLevel.Warning
                     && entry.Message.Contains(fragment, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (_gate)
        {
            _entries.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
        }
    }
}
