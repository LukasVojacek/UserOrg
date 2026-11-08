using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.Adapters;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Adapters;

public sealed class ChannelQueueAdapterTests
{
    [Fact(DisplayName = "EnqueueAsync throws ArgumentNullException when message is null")]
    public async Task Should_ThrowArgumentNullException_When_MessageIsNull()
    {
        var adapter = new ChannelQueueAdapter(10);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.EnqueueAsync(null!).AsTask());
    }

    [Fact(DisplayName = "EnqueueAsync and ConsumeAsync round-trip a message")]
    public async Task Should_ReceiveSameMessage_When_EnqueuedAndConsumed()
    {
        var adapter = new ChannelQueueAdapter(10);
        var message = new TestStepRequest(Guid.NewGuid(), "init");

        await adapter.EnqueueAsync(message, CancellationToken.None);

        StepRequest? received = null;
        await foreach (var item in adapter.ConsumeAsync(CancellationToken.None).ConfigureAwait(false))
        {
            received = item;
            break; // stop after the first item
        }

        Assert.Same(message, received);
    }

    [Fact(DisplayName = "ConsumeAsync stops yielding items when CancellationToken is pre-cancelled")]
    public async Task Should_StopConsuming_When_CancellationTokenCancelled()
    {
        var adapter = new ChannelQueueAdapter(10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var count = 0;
        try
        {
            await foreach (var _ in adapter.ConsumeAsync(cts.Token).ConfigureAwait(false))
            {
                count++;
            }
        }
        catch (OperationCanceledException)
        {
            // expected — ReadAllAsync throws when the token is pre-cancelled
        }

        Assert.Equal(0, count);
    }

    [Fact(DisplayName = "EnqueueAsync and ConsumeAsync preserve message order (FIFO)")]
    public async Task Should_PreserveMessageOrder_When_MultipleMessagesEnqueued()
    {
        var adapter = new ChannelQueueAdapter(10);
        var msg1 = new TestStepRequest(Guid.NewGuid(), "step-1");
        var msg2 = new TestStepRequest(Guid.NewGuid(), "step-2");
        var msg3 = new TestStepRequest(Guid.NewGuid(), "step-3");

        await adapter.EnqueueAsync(msg1, CancellationToken.None);
        await adapter.EnqueueAsync(msg2, CancellationToken.None);
        await adapter.EnqueueAsync(msg3, CancellationToken.None);

        var received = new List<StepRequest>();
        await foreach (var item in adapter.ConsumeAsync(CancellationToken.None).ConfigureAwait(false))
        {
            received.Add(item);
            if (received.Count == 3)
                break; // stop once we have the 3 enqueued messages
        }

        Assert.Equal(3, received.Count);
        Assert.Same(msg1, received[0]);
        Assert.Same(msg2, received[1]);
        Assert.Same(msg3, received[2]);
    }

    [Fact(DisplayName = "EnqueueAsync returns without suspending the caller when the queue is full, and records a dropped enqueue")]
    public async Task Should_NotBlockTheCaller_When_TheQueueIsFull()
    {
        using var probe = new DroppedEnqueueProbe();
        var adapter = new ChannelQueueAdapter(1);
        var fill = new TestStepRequest(Guid.NewGuid(), "fill");
        var overflow = new TestStepRequest(Guid.NewGuid(), "overflow");

        // Nothing consumes: the queue is now at capacity and stays there.
        await adapter.EnqueueAsync(fill, TestContext.Current.CancellationToken);

        // Deliberately not awaited before the assert: awaiting a blocking enqueue would hang the suite.
        // A queue that drops instead of waiting completes the ValueTask synchronously.
        var overflowEnqueue = adapter.EnqueueAsync(overflow, TestContext.Current.CancellationToken).AsTask();

        Assert.True(
            overflowEnqueue.IsCompleted,
            "EnqueueAsync must complete synchronously when the queue is full — the HTTP caller must never be suspended on queue back-pressure. It returned a pending task, i.e. it is still waiting for room.");

        await overflowEnqueue; // surfaces a faulted or cancelled completion as a failure

        Assert.True(
            probe.DroppedCount >= 1,
            $"A dropped enqueue must be recorded on a metrics instrument whose name contains 'drop'. Instruments published in this process: {probe.PublishedInstruments}.");
    }

    [Fact(DisplayName = "The overflow message is dropped rather than buffered when the queue is full")]
    public async Task Should_DropTheMessage_When_TheQueueIsFull()
    {
        var adapter = new ChannelQueueAdapter(1);
        var fill = new TestStepRequest(Guid.NewGuid(), "fill");
        var overflow = new TestStepRequest(Guid.NewGuid(), "overflow");
        var sentinel = new TestStepRequest(Guid.NewGuid(), "sentinel");

        await adapter.EnqueueAsync(fill, TestContext.Current.CancellationToken);

        var overflowEnqueue = adapter.EnqueueAsync(overflow, TestContext.Current.CancellationToken).AsTask();
        Assert.True(
            overflowEnqueue.IsCompleted,
            "EnqueueAsync must complete synchronously when the queue is full instead of waiting for room.");
        await overflowEnqueue;

        // Draining frees the slot. A parked writer would fill that slot with the overflow message;
        // a dropped message means the next thing enqueued is the next thing consumed.
        Assert.Same(fill, await ReadOneAsync(adapter, TestContext.Current.CancellationToken));

        await adapter.EnqueueAsync(sentinel, TestContext.Current.CancellationToken);
        Assert.Same(sentinel, await ReadOneAsync(adapter, TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Every message is delivered when the queue is filled exactly to capacity")]
    public async Task Should_DeliverEveryMessage_When_TheQueueIsFilledExactlyToCapacity()
    {
        const int capacity = 3;
        var adapter = new ChannelQueueAdapter(capacity);
        var sent = new List<StepRequest>();

        for (var i = 0; i < capacity; i++)
        {
            var message = new TestStepRequest(Guid.NewGuid(), $"step-{i}");
            sent.Add(message);
            await adapter.EnqueueAsync(message, TestContext.Current.CancellationToken);
        }

        var received = new List<StepRequest>();
        for (var i = 0; i < capacity; i++)
        {
            received.Add(await ReadOneAsync(adapter, TestContext.Current.CancellationToken));
        }

        Assert.Equal(sent, received);
    }

    [Fact(DisplayName = "A message enqueued after a consumer freed capacity is delivered")]
    public async Task Should_DeliverTheMessage_When_CapacityWasFreedByAConsumer()
    {
        var adapter = new ChannelQueueAdapter(1);
        var first = new TestStepRequest(Guid.NewGuid(), "first");
        var second = new TestStepRequest(Guid.NewGuid(), "second");

        await adapter.EnqueueAsync(first, TestContext.Current.CancellationToken);
        Assert.Same(first, await ReadOneAsync(adapter, TestContext.Current.CancellationToken));

        await adapter.EnqueueAsync(second, TestContext.Current.CancellationToken);
        Assert.Same(second, await ReadOneAsync(adapter, TestContext.Current.CancellationToken));
    }

    private static async Task<StepRequest> ReadOneAsync(ChannelQueueAdapter adapter, CancellationToken ct)
    {
        await foreach (var item in adapter.ConsumeAsync(ct).ConfigureAwait(false))
        {
            return item;
        }

        throw new InvalidOperationException("The queue completed without yielding a message.");
    }
}
