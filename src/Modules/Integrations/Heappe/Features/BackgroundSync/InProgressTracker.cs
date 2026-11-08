using System.Collections.Concurrent;

namespace Heappe.Features.BackgroundSync;

internal sealed class InProgressTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _inProgress = new();
    private readonly TimeSpan _timeout;

    public InProgressTracker(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public bool TryMarkInProgress(string key, DateTime startedAt) => _inProgress.TryAdd(key, startedAt);

    public bool IsInProgress(string key) => _inProgress.ContainsKey(key);

    public bool HasTimedOut(string key)
    {
        if (!_inProgress.TryGetValue(key, out var inProgressSince))
            return false;

        return DateTime.UtcNow - inProgressSince > _timeout;
    }

    public void UpdateStartTime(string key, DateTime newStartedAt) => _inProgress[key] = newStartedAt;

    public void MarkCompleted(string key) => _inProgress.TryRemove(key, out _);

    public TimeSpan GetTimeoutDuration() => _timeout;
}