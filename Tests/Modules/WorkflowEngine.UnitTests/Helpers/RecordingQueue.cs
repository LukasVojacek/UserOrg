using System.Runtime.CompilerServices;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// An <see cref="IQueuePort"/> that records what was enqueued and hands nothing back out.
/// <para>
/// Standing in for the channel is the point: the in-process queue is exactly the component whose loss these
/// tests simulate. <see cref="Clear"/> is "the process restarted and the channel went with it".
/// </para>
/// </summary>
internal sealed class RecordingQueue : IQueuePort
{
    private readonly Lock _gate = new();
    private readonly List<StepRequest> _messages = [];

    public IReadOnlyList<StepRequest> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    /// <summary>Drops everything enqueued so far — the in-memory queue dying with the process.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }

    public ValueTask EnqueueAsync(StepRequest message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            _messages.Add(message);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Never yields: no worker consumes in these tests, they assert on what recovery wrote.</summary>
    public async IAsyncEnumerable<StepRequest> ConsumeAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }
}
