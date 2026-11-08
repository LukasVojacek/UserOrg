using Microsoft.Extensions.Logging;

namespace Waldur.Sync.Tests.TestSupport;

/// <summary>
/// One captured log entry: its level, event id, rendered message, and the merged
/// <see cref="ILogger.BeginScope{TState}"/> key/values active when it was written (outer-to-inner,
/// inner wins) — so a test can assert on the TraceId / SpanId / ExternalId correlation the worker
/// stamps via <c>BeginScope</c> (ADR-0066).
/// </summary>
internal readonly record struct LogEntry(
    LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> Scope)
{
    /// <summary>The scope value for <paramref name="key"/> as a string, or <c>null</c> if absent.</summary>
    public string? ScopeValue(string key) => Scope.TryGetValue(key, out var v) ? v?.ToString() : null;
}

/// <summary>
/// A minimal <see cref="ILogger{T}"/> that records every entry (all levels enabled) so a test can
/// assert on the level / event id / message of the worker's structured log markers, plus the scope
/// key/values active at each log site. Scopes are tracked per async-flow (AsyncLocal) so concurrent
/// fan-out items keep distinct scope stacks.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly AsyncLocal<ScopeFrame?> _current = new();

    public List<LogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        var frame = new ScopeFrame(this, state, _current.Value);
        _current.Value = frame;
        return frame;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry(logLevel, eventId, formatter(state, exception), CaptureScope());
        lock (Entries)
        {
            Entries.Add(entry);
        }
    }

    private IReadOnlyDictionary<string, object?> CaptureScope()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Walk outermost → innermost so an inner scope's value overrides an outer one's.
        var frames = new List<ScopeFrame>();
        for (var frame = _current.Value; frame is not null; frame = frame.Parent)
        {
            frames.Add(frame);
        }

        frames.Reverse();
        foreach (var frame in frames)
        {
            if (frame.State is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    values[pair.Key] = pair.Value;
                }
            }
        }

        return values;
    }

    private sealed class ScopeFrame(CapturingLogger<T> owner, object state, ScopeFrame? parent) : IDisposable
    {
        public object State { get; } = state;

        public ScopeFrame? Parent { get; } = parent;

        public void Dispose() => owner._current.Value = Parent;
    }
}
