namespace Core.Domain.User;

/// <summary>Marker for domain events scoped to a single user identity, exposing the user's <see cref="IdpId"/>.</summary>
public interface IUserScopedEvent
{
    IdpId IdentityProviderId { get; }
}

/// <summary>Domain event raised when a new user is created in the system.</summary>
public sealed record UserCreatedEvent(UserName UserName, Email Email, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user is soft-deleted from the system.</summary>
public sealed record UserDeletedEvent(UserName UserName, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user is promoted to ProviderAdmin and assigned to one or more providers.</summary>
public sealed record UserPromotedToProviderAdminEvent(UserName UserName, IReadOnlyList<ProviderId> AssignedProviderIds, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a ProviderAdmin is demoted back to the platform User role.</summary>
public sealed record UserDemotedToUserEvent(UserName UserName, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user is assigned to an additional provider.</summary>
public sealed record UserAssignedToProviderEvent(UserName UserName, ProviderId ProviderId, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user is unassigned from a provider.</summary>
public sealed record UserUnassignedFromProviderEvent(UserName UserName, ProviderId ProviderId, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user's display name is changed.</summary>
public sealed record UserNameUpdatedEvent(UserName OldUserName, UserName NewUserName, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user's e-mail address is changed.</summary>
public sealed record UserEmailUpdatedEvent(Email OldEmail, Email NewEmail, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a soft-deleted user is reactivated.</summary>
public sealed record UserReactivatedEvent(UserName UserName, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;

/// <summary>Domain event raised when a user updates their preferences.</summary>
public sealed record UserPreferencesUpdatedEvent(UserName UserName, IdpId IdentityProviderId) : IDomainEvent, IUserScopedEvent;