using System.Collections.Frozen;
using System.Reflection;

using Core.Domain.Primitives;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Maps a stored <see cref="OutboxMessage.Type"/> string (the event's <see cref="Type.FullName"/>) back to
/// its CLR <see cref="Type"/> so the dispatcher (O4) can deserialize the payload. Only <see cref="IDurableEvent"/>
/// types ever reach the outbox, so the map is built once over the durable events in <c>Core.Domain</c>.
/// </summary>
internal interface IOutboxEventTypeResolver
{
    /// <summary>
    /// Resolves a stored type name to its CLR <see cref="Type"/>.
    /// </summary>
    /// <param name="typeName">The <see cref="Type.FullName"/> stored on the outbox row.</param>
    /// <returns>The CLR type implementing <see cref="IDurableEvent"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The name does not map to a known durable event — a poison message that must be surfaced, not swallowed.
    /// </exception>
    Type Resolve(string typeName);
}

/// <inheritdoc />
internal sealed class OutboxEventTypeResolver : IOutboxEventTypeResolver
{
    private readonly FrozenDictionary<string, Type> _byFullName;

    public OutboxEventTypeResolver()
    {
        // Scan Core.Domain once for non-abstract durable events; their FullName is the stable discriminator
        // written by OutboxRouting.ToOutboxMessage.
        _byFullName = typeof(IDurableEvent).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDurableEvent).IsAssignableFrom(t))
            .ToFrozenDictionary(t => t.FullName!, t => t);
    }

    public Type Resolve(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        return _byFullName.TryGetValue(typeName, out var type)
            ? type
            : throw new InvalidOperationException(
                $"Unknown outbox event type '{typeName}'. No non-abstract {nameof(IDurableEvent)} in '{typeof(IDurableEvent).Assembly.GetName().Name}' maps to it (poison message).");
    }
}