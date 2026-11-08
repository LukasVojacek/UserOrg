namespace Core.Domain.Provider;

/// <summary>Domain event raised when a new provider is registered.</summary>
public sealed record ProviderCreatedEvent(ProviderName Name) : IDomainEvent;

/// <summary>Domain event raised when a provider is soft-deleted.</summary>
public sealed record ProviderDeletedEvent(ProviderName Name) : IDomainEvent;

/// <summary>Domain event raised when a location is added to a provider.</summary>
public sealed record LocationAddedEvent(ProviderName ProviderName, LocationName LocationName) : IDomainEvent;

/// <summary>Domain event raised when a location is removed from a provider.</summary>
public sealed record LocationRemovedEvent(ProviderName ProviderName, LocationName LocationName) : IDomainEvent;

/// <summary>Marker for domain events scoped to a single resource, exposing its natural key (ADR-0044 L2.6).</summary>
public interface IResourceScopedEvent
{
    ProjectResourceKey Resource { get; }
}

/// <summary>
/// Domain event raised when a resource assignment (AssignedLocation) is created, updated or removed.
/// Carries the allocated resource's natural key (ADR-0044 L2.6) so the post-commit handler can resolve the
/// affected projects and trigger their HEAppE synchronization (ADR-0016).
/// </summary>
public sealed record ResourceAssignmentChangedEvent(ProjectResourceKey Resource) : IDurableEvent, IResourceScopedEvent;

/// <summary>
/// Domain event raised when a resource becomes available for assignment (DEC-4). Durable so the Core handler
/// can reliably resume every assignment request parked on the <c>ResourceAvailability</c> gate for this
/// resource's natural key (ADR-0044 L2.6).
/// </summary>
public sealed record ProjectResourceActivatedEvent(ProjectResourceKey Resource) : IDurableEvent, IResourceScopedEvent;