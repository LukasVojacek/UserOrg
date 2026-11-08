using System.Diagnostics.Metrics;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Adapters;

/// <summary>
/// In-process implementation of <see cref="IQueuePort"/> backed by a bounded
/// <see cref="Channel{T}"/>. Suitable for single-process deployments.
/// Replace with a durable message broker adapter (RabbitMQ, Azure Service Bus, etc.)
/// for multi-instance or durable-message scenarios.
/// </summary>
/// <remarks>
/// <see cref="EnqueueAsync"/> never suspends the caller: a stalled single <c>RequestWorker</c> would
/// otherwise park the HTTP request that submitted the step. When the channel is full the message is
/// dropped rather than queued — <c>workflow.Requests</c> is the durability boundary, not the channel
/// (ADR-0100). But startup recovery only runs once at boot, so a drop while the process is already up and
/// running — including a mid-flow enqueue, not just the initial one — strands that request until the next
/// restart; only a Warning marks it in the meantime. At the default capacity this is rare and still beats a
/// hung caller, but it is not a free safety net: see ADR-0100's Consequences for what to alert on.
/// </remarks>
internal sealed class ChannelQueueAdapter : IQueuePort
{
    /// <summary>
    /// Self-owned meter — <c>ChannelQueueAdapter</c> is constructed with plain <c>new</c> in several call
    /// sites (including every unit test), so metrics cannot depend on an injected <c>IMeterFactory</c>.
    /// </summary>
    private static readonly Meter Meter = new("WorkflowEngine.Core.Queue");

    private static readonly Counter<long> DroppedCounter =
        Meter.CreateCounter<long>("workflowengine.queue.enqueue_dropped", unit: "{message}",
            description: "Number of step messages dropped because the queue was full.");

    private readonly Channel<StepRequest> _channel;
    private readonly ILogger<ChannelQueueAdapter> _logger;
    private readonly ObservableGauge<int> _depthGauge;

    public ChannelQueueAdapter(int capacity, ILogger<ChannelQueueAdapter>? logger = null)
    {
        _channel = Channel.CreateBounded<StepRequest>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        _logger = logger ?? NullLogger<ChannelQueueAdapter>.Instance;

        _depthGauge = Meter.CreateObservableGauge(
            "workflowengine.queue.depth",
            () => _channel.Reader.Count,
            unit: "{message}",
            description: "Current number of step messages waiting in the queue.");
    }

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(StepRequest message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // TryWrite never suspends the caller. FullMode is Wait, so TryWrite returning false means the
        // channel is genuinely at capacity right now — not a transient race with a reader.
        if (_channel.Writer.TryWrite(message))
        {
            return ValueTask.CompletedTask;
        }

        DroppedCounter.Add(1);

        // Warning: a measurable precursor, not yet an incident. Startup recovery is one-shot at boot, so
        // this request is stranded until the next restart — not until recovery next runs; a queue that
        // stays full is heading toward sustained submission latency, so this is worth a record for whoever
        // watches queue health — not an Error, because nothing failed here. Alert on the
        // workflowengine.queue.enqueue_dropped counter, not on this line.
        _logger.LogWarning(
            "WorkflowEngine queue is full — dropping step {StepType} for request {RequestId}. The request is stranded until the process next restarts and startup recovery runs; see the workflowengine.queue.enqueue_dropped counter.",
            message.GetType().Name,
            message.RequestId);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<StepRequest> ConsumeAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}