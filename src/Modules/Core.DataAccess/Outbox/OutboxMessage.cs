namespace Core.DataAccess.Outbox;

/// <summary>
/// Transactional outbox row (ADR-0028). One row is written in the same transaction as the aggregate
/// change that produced the event; a dispatcher later delivers it to completion (ADR-0040).
/// Plain infrastructure entity — no domain base type, no soft-delete.
/// </summary>
internal sealed class OutboxMessage
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Fully-qualified event type name used to resolve the payload on dispatch.</summary>
    public string Type { get; set; } = null!;

    /// <summary>Serialized event payload.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>When the event occurred / the row was enqueued.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>When the message was successfully dispatched; <c>null</c> while still pending.</summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    /// <summary>Number of delivery attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Timestamp of the most recent delivery attempt (ADR-0040); <c>null</c> before the first attempt.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Last delivery error, if any.</summary>
    public string? Error { get; set; }
}