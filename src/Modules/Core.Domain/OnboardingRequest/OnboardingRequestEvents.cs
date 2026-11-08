namespace Core.Domain.OnboardingRequest;

/// <summary>Domain event raised when a new onboarding request is submitted.</summary>
public sealed record OnboardingRequestCreatedEvent(ProjectShortName Project, IdpId Requester, Email ContactEmail) : IDomainEvent;

/// <summary>Domain event raised when an onboarding request's state transitions.</summary>
public sealed record OnboardingRequestStateTransitionedEvent(ProjectShortName Project, IdpId Requester, int FromState, int ToState) : IDomainEvent;

/// <summary>Domain event raised when an onboarding request is soft-deleted.</summary>
public sealed record OnboardingRequestDeletedEvent(ProjectShortName Project, IdpId Requester) : IDomainEvent;