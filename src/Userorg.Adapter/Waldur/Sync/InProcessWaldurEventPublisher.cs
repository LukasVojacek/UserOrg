using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.DependencyInjection;

using Waldur.Contracts;
using Waldur.Contracts.Events;

namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// In-process implementation of <see cref="IWaldurEventPublisher"/> (ADR-0056/0057, plan §4.1).
/// Dispatch is a static switch over the closed <see cref="WaldurIntegrationEvent"/> contract set,
/// so the handler's generic type is bound at compile time, with no reflection. Each published event runs
/// in its own DI scope (a fresh <c>IUserOrgDbContext</c>) so concurrent instance workers never share
/// EF state. The returned task completes only once the resolved handler completed, so completion =
/// ack; a handler exception propagates unchanged = not acked.
/// </summary>
internal sealed class InProcessWaldurEventPublisher(IServiceScopeFactory scopeFactory) : IWaldurEventPublisher
{
    /// <inheritdoc />
    public async Task<Result<Error>> PublishAsync(WaldurIntegrationEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        return await (evt switch
        {
            ProviderCreated e => Handle(sp, e, ct),
            ProviderUpdated e => Handle(sp, e, ct),
            ProjectCreated e => Handle(sp, e, ct),
            ProjectUpdated e => Handle(sp, e, ct),
            UserAssigned e => Handle(sp, e, ct),
            UserRoleChanged e => Handle(sp, e, ct),
            UserRemoved e => Handle(sp, e, ct),
            StorageConfigurationChanged e => Handle(sp, e, ct),
            _ => throw new NotSupportedException($"No dispatch arm for {evt.GetType().Name}.")
        });
    }

    /// <summary>
    /// Resolves the handler for <typeparamref name="TEvent"/> from the per-event scope and returns
    /// its task, so both resolution and handler failures surface to the caller unchanged.
    /// </summary>
    private static Task<Result<Error>> Handle<TEvent>(IServiceProvider sp, TEvent evt, CancellationToken ct)
        where TEvent : WaldurIntegrationEvent
        => sp.GetRequiredService<IWaldurEventHandler<TEvent>>().HandleAsync(evt, ct);
}