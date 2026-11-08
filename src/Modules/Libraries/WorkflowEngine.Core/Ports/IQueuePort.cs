using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Ports;

/// <summary>
/// Driven port — abstraction over the message queue used to pass <see cref="StepRequest"/> messages
/// between the application layer and the background workers.
/// The default in-process adapter is <c>ChannelQueueAdapter</c>.
/// </summary>
public interface IQueuePort
{
    /// <summary>Writes a step message onto the queue for the worker to consume.</summary>
    ValueTask EnqueueAsync(StepRequest message, CancellationToken ct = default);

    /// <summary>
    /// Streams step messages from the queue as an async sequence.
    /// The caller is responsible for processing each message and acknowledging completion.
    /// The sequence ends only when <paramref name="ct"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<StepRequest> ConsumeAsync(CancellationToken ct);
}