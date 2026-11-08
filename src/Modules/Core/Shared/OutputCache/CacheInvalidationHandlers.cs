namespace Core.Shared.OutputCache;

/// <summary>
/// Evicts the UserInfo cache for any user-scoped domain event. One generic handler closed over each
/// event type (registered per closed generic in <c>AddCore</c>) so each registration
/// implements exactly one <see cref="IDomainEventHandler{TEvent}"/> — avoiding default-interface-method
/// ambiguity that would arise from one class handling many event types.
/// </summary>
internal sealed class UserInfoCacheEvictionHandler<TEvent>(IOutputCacheInvalidator cache)
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent, IUserScopedEvent
{
    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
        => cache.EvictAsync(CacheTags.UserInfo(domainEvent.IdentityProviderId.Value), cancellationToken).AsTask();
}

/// <summary>Evicts the Project cache for any project-scoped domain event.</summary>
internal sealed class ProjectCacheEvictionHandler<TEvent>(IOutputCacheInvalidator cache)
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent, IProjectScopedEvent
{
    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
        => cache.EvictAsync(CacheTags.Project(domainEvent.ShortName.Value), cancellationToken).AsTask();
}

/// <summary>
/// Evicts the resource-tagged responses for any resource-scoped domain event. The templates endpoint
/// tags each response with <c>resource:{guid}</c> (the surrogate), so the event's natural key is resolved
/// to that surrogate here to build the matching tag (ADR-0016 / 0041 L2.6). The Guid is a storage detail
/// confined to this cache-tag boundary. A key that no longer resolves (resource removed) evicts nothing.
/// </summary>
internal sealed class ResourceCacheEvictionHandler<TEvent>(IOutputCacheInvalidator cache, IResourceAssignmentQuery query)
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent, IResourceScopedEvent
{
    public async Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (await query.GetResourceSurrogateAsync(domainEvent.Resource, cancellationToken) is { } surrogate)
            await cache.EvictAsync(CacheTags.ProjectResource(surrogate), cancellationToken);
    }
}