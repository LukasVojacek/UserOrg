using System.Text.Json;

using Core.DataAccess.Outbox;
using Core.Domain.Primitives;
using Core.Domain.Project;
using Core.Domain.Provider;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Tests for the durable/best-effort split and the event→<see cref="OutboxMessage"/> serialization
/// (block O2). The real cross-context transaction needs a SQL connection and is verified against a real
/// DB by the developer/CI (ADR-0041); these tests cover the routing + payload round-trip, which is what
/// the in-memory provider can assert. The round-trip is the gate for value-object serialization problems.
/// </summary>
public sealed class OutboxRoutingTests
{
    private static ResourceAssignmentChangedEvent SampleResourceEvent() =>
        new(new ProjectResourceKey(ProviderName.From("acme"), ResourceName.From("gpu-res")));

    private static ProjectResourceAssignmentChangedEvent SampleProjectResourceEvent() =>
        new(ProjectShortName.Create("proj-x"), ResourceName.Create("cluster-a"));

    /// <summary>A best-effort event with no <see cref="IDurableEvent"/> marker.</summary>
    private sealed record FakeBestEffortEvent(string Note) : IDomainEvent;

    [Fact]
    public void Should_RoundTripPayload_When_ResourceAssignmentChangedEventSerialized()
    {
        var evt = SampleResourceEvent();

        var json = JsonSerializer.Serialize(evt, evt.GetType(), OutboxRouting.OutboxSerialization);
        var restored = (ResourceAssignmentChangedEvent)JsonSerializer.Deserialize(
            json, evt.GetType(), OutboxRouting.OutboxSerialization)!;

        Assert.Equal(evt, restored);
    }

    [Fact]
    public void Should_RoundTripPayload_When_ProjectResourceAssignmentChangedEventSerialized()
    {
        var evt = SampleProjectResourceEvent();

        var json = JsonSerializer.Serialize(evt, evt.GetType(), OutboxRouting.OutboxSerialization);
        var restored = (ProjectResourceAssignmentChangedEvent)JsonSerializer.Deserialize(
            json, evt.GetType(), OutboxRouting.OutboxSerialization)!;

        Assert.Equal(evt, restored);
    }

    [Fact]
    public void Should_MarkResourceAssignmentEventsDurable_When_Inspected()
    {
        Assert.IsAssignableFrom<IDurableEvent>(SampleResourceEvent());
        Assert.IsAssignableFrom<IDurableEvent>(SampleProjectResourceEvent());
    }

    [Fact]
    public void Should_NotMarkOtherEventsDurable_When_Inspected()
    {
        Assert.IsNotAssignableFrom<IDurableEvent>(new FakeBestEffortEvent("x"));
        // A real best-effort domain event sanity check.
        Assert.IsNotAssignableFrom<IDurableEvent>(new ProjectCreatedEvent(
            ProjectShortName.Create("proj-y"), ProjectName.Create("Project Y"), new UserId(Guid.CreateVersion7())));
    }

    [Fact]
    public void Should_SplitDurableFromBestEffort_When_Partitioned()
    {
        var durableA = SampleResourceEvent();
        var durableB = SampleProjectResourceEvent();
        var bestEffort = new FakeBestEffortEvent("note");

        var (durable, best) = OutboxRouting.Partition([durableA, bestEffort, durableB]);

        Assert.Equal([durableA, durableB], durable);
        Assert.Equal<IDomainEvent>([bestEffort], best);
    }

    [Fact]
    public void Should_ProduceTypedNonEmptyMessage_When_ToOutboxMessageCalled()
    {
        var evt = SampleResourceEvent();

        var message = OutboxRouting.ToOutboxMessage(evt);

        Assert.Equal(typeof(ResourceAssignmentChangedEvent).FullName, message.Type);
        Assert.False(string.IsNullOrWhiteSpace(message.Payload));
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.DispatchedAt);
        Assert.NotEqual(Guid.Empty, message.Id);
    }
}
