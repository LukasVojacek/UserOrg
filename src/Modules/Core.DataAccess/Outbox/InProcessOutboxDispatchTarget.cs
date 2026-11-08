using System.Text.Json;

using Core.Domain.Primitives;

using Microsoft.Extensions.DependencyInjection;

namespace Core.DataAccess.Outbox;

/// <summary>
/// In-process implementation of <see cref="IOutboxDispatchTarget"/> (ADR-0040): resolves the stored event
/// type, deserializes the payload with the same <see cref="OutboxRouting.OutboxSerialization"/> options that
/// wrote it, and runs every matching <see cref="IDomainEventHandler{TEvent}"/> in a fresh DI scope — the
/// exact handler-resolution pattern of <c>DomainEventDispatcherService</c>. Any handler that throws
/// propagates out so the dispatcher retries the row.
/// <para>
/// NOTE: the durable <c>ResourceAssignmentChangedEvent</c> also has a best-effort cache-eviction handler.
/// Running <i>all</i> handlers here means a cache-evict failure would re-attempt the whole row, re-running
/// the idempotent HEAppE sync (harmless — keyed by (project, resource) + upsert, ADR-0040). Mark-done means
/// every handler succeeded; a partial failure retries. This is intentional, not a leak.
/// </para>
/// </summary>
internal sealed class InProcessOutboxDispatchTarget(
    IServiceScopeFactory scopeFactory,
    IOutboxEventTypeResolver resolver) : IOutboxDispatchTarget
{
    public async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var eventType = resolver.Resolve(message.Type);
        var evt = (IDomainEvent)JsonSerializer.Deserialize(message.Payload, eventType, OutboxRouting.OutboxSerialization)!;

        // Fresh scope per dispatch so handlers can use scoped services (e.g., DbContext) — mirrors
        // DomainEventDispatcherService exactly.
        await using var scope = scopeFactory.CreateAsyncScope();

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = scope.ServiceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is IDomainEventHandler h)
                await h.HandleAsync(evt, ct);
        }
    }
}