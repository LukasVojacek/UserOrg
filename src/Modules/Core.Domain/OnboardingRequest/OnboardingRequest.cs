using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.OnboardingRequest;

/// <summary>
/// OnboardingRequest aggregate root — a user asking to join a project as a member (ADR-0046).
/// Standalone aggregate holding only natural keys. The surrogate <c>Id</c>, the <c>ProjectId</c>/<c>UserId</c>
/// FKs and the audit columns are EF shadow members in Core.DataAccess; they are deliberately absent here so
/// the domain model is free of persistence concerns.
/// </summary>
public sealed class OnboardingRequest : AggregateRoot, ISoftDeletableEntity
{
    /// <summary>Project the requester wants to join (natural key — not a Guid).</summary>
    public ProjectShortName Project { get; private set; }

    /// <summary>Requester's identity-provider id (natural key — not a Guid).</summary>
    public IdpId Requester { get; private set; }

    /// <summary>Requester contact email, for notifications.</summary>
    public Email ContactEmail { get; private set; }

    public OnboardingRequestState State { get; private set; } = OnboardingRequestState.Pending;

    /// <summary>GUID-free public identifier a v2 client addresses this request by (ADR-0024).</summary>
    public OnboardingRequestSlug? Slug { get; private set; }

    public bool IsDeleted { get; private set; }

    // EF materialization + factory only; closes the implicit public parameterless constructor so
    // `new OnboardingRequest()` cannot bypass Create.
    private OnboardingRequest() { }

    public static OnboardingRequest Create(
        ProjectShortName project,
        IdpId requester,
        Email contactEmail,
        OnboardingRequestSlug? slug = null)
    {
        // Always starts Pending; subsequent states go through TransitionToState so the state machine and
        // its OnboardingRequestStateTransitionedEvent are never bypassed.
        var request = new OnboardingRequest
        {
            Project = project,
            Requester = requester,
            ContactEmail = contactEmail,
            State = OnboardingRequestState.Pending,
            Slug = slug,
            IsDeleted = false,
        };

        request.AddDomainEvent(new OnboardingRequestCreatedEvent(project, requester, contactEmail));
        return request;
    }

    /// <summary>Transitions the request to a new state, enforcing the state machine.</summary>
    private Result<Error> TransitionToState(OnboardingRequestState newState)
    {
        if (!State.CanTransitionTo(newState))
        {
            return Result<Error>.Failure(OnboardingRequestErrors.InvalidState(State, newState));
        }

        var oldState = State.Value;
        State = newState;
        AddDomainEvent(new OnboardingRequestStateTransitionedEvent(Project, Requester, oldState, newState.Value));
        return Result<Error>.Success();
    }

    /// <summary>Settles the request by accepting it (terminal). Enforces the state machine.</summary>
    public Result<Error> Accept() => TransitionToState(OnboardingRequestState.Accepted);

    /// <summary>Settles the request by rejecting it (terminal). Enforces the state machine.</summary>
    public Result<Error> Reject() => TransitionToState(OnboardingRequestState.Rejected);

    public Result<Error> Delete()
    {
        if (IsDeleted)
        {
            return Result<Error>.Failure(OnboardingRequestErrors.AlreadyDeleted());
        }

        IsDeleted = true;
        AddDomainEvent(new OnboardingRequestDeletedEvent(Project, Requester));
        return Result<Error>.Success();
    }

    public Result<Error> Restore()
    {
        if (!IsDeleted)
        {
            return Result<Error>.Failure(OnboardingRequestErrors.NotDeleted());
        }

        IsDeleted = false;
        return Result<Error>.Success();
    }
}