using Core.DataAccess.Outbox;
using Core.Domain.Primitives;
using Core.Domain.Project;

using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Unit tests for the in-process dispatch target (block O4). Builds a real <see cref="OutboxMessage"/> via the
/// same <see cref="OutboxRouting.ToOutboxMessage"/> the writer uses (so the payload + type are real), registers
/// a handler in a real <see cref="ServiceProvider"/> scope factory, and asserts the deserialized event is
/// handed to the handler. A throwing handler must propagate so the dispatcher retries the row.
/// <para>
/// A counting fake (not Moq) implements the generic <see cref="IDomainEventHandler{TEvent}.HandleAsync(TEvent, CancellationToken)"/>
/// directly: the dispatcher invokes the non-generic <c>IDomainEventHandler.HandleAsync</c> default interface
/// method, which forwards into the generic override — so the fake observes the real, fully-deserialized event.
/// </para>
/// </summary>
public sealed class InProcessOutboxDispatchTargetTests
{
    private static readonly OutboxEventTypeResolver Resolver = new();

    private static ProjectResourceAssignmentChangedEvent SampleEvent() =>
        new(ProjectShortName.Create("proj-x"), ResourceName.Create("cluster-a"));

    private static InProcessOutboxDispatchTarget BuildTarget(
        IDomainEventHandler<ProjectResourceAssignmentChangedEvent> handler, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<ProjectResourceAssignmentChangedEvent>>(handler);
        provider = services.BuildServiceProvider();
        return new InProcessOutboxDispatchTarget(provider.GetRequiredService<IServiceScopeFactory>(), Resolver);
    }

    private sealed class CountingHandler : IDomainEventHandler<ProjectResourceAssignmentChangedEvent>
    {
        public int Calls { get; private set; }
        public ProjectResourceAssignmentChangedEvent? Last { get; private set; }

        public Task HandleAsync(ProjectResourceAssignmentChangedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = domainEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<ProjectResourceAssignmentChangedEvent>
    {
        public Task HandleAsync(ProjectResourceAssignmentChangedEvent domainEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("handler boom");
    }

    [Fact]
    public async Task Should_DeserializeAndInvokeHandler_When_Dispatched()
    {
        var evt = SampleEvent();
        var message = OutboxRouting.ToOutboxMessage(evt);

        var handler = new CountingHandler();
        var target = BuildTarget(handler, out var provider);
        using var _ = provider;

        await target.DispatchAsync(message, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(evt, handler.Last);
    }

    [Fact]
    public async Task Should_Propagate_When_HandlerThrows()
    {
        var message = OutboxRouting.ToOutboxMessage(SampleEvent());

        var target = BuildTarget(new ThrowingHandler(), out var provider);
        using var _ = provider;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => target.DispatchAsync(message, CancellationToken.None));
    }
}
