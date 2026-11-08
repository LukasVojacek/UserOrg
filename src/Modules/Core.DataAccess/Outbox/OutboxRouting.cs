using System.Text.Json;

using Core.Domain.Primitives;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Routing + serialization for the transactional outbox (ADR-0028, ADR-0040). Splits a batch of
/// collected domain events into the durable subset (persisted to the outbox table in the same
/// transaction) and the best-effort subset (dispatched on the in-memory channel), and serializes a
/// durable event into an <see cref="OutboxMessage"/>. The <see cref="OutboxSerialization"/> options
/// are the single source of truth so the dispatcher (O4) deserializes with the exact same settings.
/// </summary>
internal static class OutboxRouting
{
    /// <summary>
    /// JSON options used to (de)serialize the outbox <see cref="OutboxMessage.Payload"/>. The value
    /// objects carried by the durable events ship their own <c>[JsonConverter]</c> attributes
    /// (<c>ValueObjectJsonConverter</c>), so they round-trip through their underlying primitive with
    /// no extra converters registered here.
    /// </summary>
    internal static JsonSerializerOptions OutboxSerialization { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Partitions collected events into durable (outbox) and best-effort (in-memory channel) subsets.
    /// </summary>
    internal static (List<IDomainEvent> Durable, List<IDomainEvent> BestEffort) Partition(
        IReadOnlyCollection<IDomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var durable = new List<IDomainEvent>();
        var bestEffort = new List<IDomainEvent>();

        foreach (var evt in events)
        {
            if (evt is IDurableEvent)
            {
                durable.Add(evt);
            }
            else
            {
                bestEffort.Add(evt);
            }
        }

        return (durable, bestEffort);
    }

    /// <summary>
    /// Serializes a durable domain event into an undispatched <see cref="OutboxMessage"/>. The
    /// <see cref="OutboxMessage.Type"/> is the event's <see cref="Type.FullName"/> — a stable
    /// discriminator the dispatcher (O4) maps back to the CLR type via a registry.
    /// </summary>
    internal static OutboxMessage ToOutboxMessage(IDomainEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var type = e.GetType();

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type.FullName!,
            Payload = JsonSerializer.Serialize(e, type, OutboxSerialization),
            OccurredAt = DateTimeOffset.UtcNow,
            Attempts = 0,
        };
    }
}