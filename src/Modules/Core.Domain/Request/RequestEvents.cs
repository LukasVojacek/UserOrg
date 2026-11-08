namespace Core.Domain.Request;

/// <summary>Domain event raised when a new request is submitted.</summary>
public sealed record RequestCreatedEvent(Email ContactEmail, ProjectId ProjectId, UserId UserId) : IDomainEvent;

/// <summary>Domain event raised when request state transitions.</summary>
public sealed record RequestStateTransitionedEvent(Email? ContactEmail, int FromState, int ToState) : IDomainEvent;

/// <summary>Domain event raised when a request is soft-deleted.</summary>
public sealed record RequestDeletedEvent(Email? ContactEmail) : IDomainEvent;