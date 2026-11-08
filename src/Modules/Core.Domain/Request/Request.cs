using System.Collections.Immutable;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Request;

/// <summary>
/// Request Aggregate Root.
/// Id, CreatedAt and ModifiedAt are EF shadow properties. Cross-aggregate references
/// (Project, User, ProjectResource, Provider) are held as typed ids.
/// The <c>_requestActionLinks</c> backing field is mapped by EF; <see cref="ActionLinks"/> is a read-only view.
/// </summary>
public sealed class Request : AggregateRoot, ISoftDeletableEntity
{
    private readonly ICollection<ActionLink> _requestActionLinks = [];

    /// <summary>Contact email. Null when not set (the column is nullable in the DB).</summary>
    public Email? ContactEmail { get; private set; }

    public RequestType Type { get; private set; }

    public RequestState State { get; private set; } = RequestState.Pending;

    public ProjectId ProjectId { get; private set; }

    public UserId UserId { get; private set; }

    public ProjectResourceId? ProjectResourceId { get; private set; }

    public ResourceName? ProjectResourceName { get; private set; }

    public ProviderId? ProviderId { get; private set; }

    /// <summary>
    /// GUID-free public identifier a v2 client addresses this request by (ADR-0024). Null for v1-created
    /// requests, which are handled on the legacy path; v2 generates one via <see cref="RequestSlug"/>.
    /// </summary>
    public string? Slug { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>Action links associated with the request.</summary>
    public ImmutableList<ActionLink> ActionLinks => [.. _requestActionLinks];

    // EF materialization + factory only; closes the implicit public parameterless constructor so
    // `new Request()` cannot bypass Create.
    private Request() { }

    public static Request Create(
        Email contactEmail,
        ProjectId projectId,
        UserId userId,
        RequestType type = default,
        ProjectResourceId? projectResourceId = null,
        ResourceName? projectResourceName = null,
        ProviderId? providerId = null,
        string? slug = null)
    {
        if (type == default)
            type = RequestType.Default;

        // A request always starts Pending; subsequent states go through TransitionToState so the state
        // machine and its RequestStateTransitionedEvent are never bypassed.
        var request = new Request
        {
            ContactEmail = contactEmail,
            Type = type,
            State = RequestState.Pending,
            ProjectId = projectId,
            UserId = userId,
            ProjectResourceId = projectResourceId,
            ProjectResourceName = projectResourceName,
            ProviderId = providerId,
            Slug = slug,
            IsDeleted = false,
        };

        request.AddDomainEvent(new RequestCreatedEvent(contactEmail, projectId, userId));
        return request;
    }

    /// <summary>Transitions the request to a new state, enforcing the state machine.</summary>
    public Result<Error> TransitionToState(RequestState newState)
    {
        if (!State.CanTransitionTo(newState))
        {
            return Result<Error>.Failure(RequestErrors.InvalidStateTransition(State, newState));
        }

        var oldState = State.Value;
        State = newState;
        AddDomainEvent(new RequestStateTransitionedEvent(ContactEmail, oldState, newState.Value));
        return Result<Error>.Success();
    }

    /// <summary>Settles the request by accepting it (terminal). Enforces the state machine.</summary>
    public Result<Error> Accept() => TransitionToState(RequestState.Accepted);

    /// <summary>Settles the request by rejecting it (terminal). Enforces the state machine.</summary>
    public Result<Error> Reject() => TransitionToState(RequestState.Rejected);

    /// <summary>
    /// Soft-deletes every active action link of the request — a link is single-use, so handling the
    /// request through one link invalidates its siblings.
    /// </summary>
    public void InvalidateActionLinks()
    {
        foreach (var link in _requestActionLinks)
        {
            if (!link.IsDeleted)
            {
                link.Delete();
            }
        }
    }

    public void UpdateResource(ProjectResourceId? projectResourceId, ResourceName? projectResourceName)
    {
        ProjectResourceId = projectResourceId;
        ProjectResourceName = projectResourceName;
    }

    public void UpdateProvider(ProviderId? providerId) => ProviderId = providerId;

    public ActionLink AddActionLink(int action, string? link, DateTimeOffset validTo)
    {
        var actionLink = ActionLink.Create(action, link, validTo);
        _requestActionLinks.Add(actionLink);
        return actionLink;
    }

    public Result<Error> Delete()
    {
        if (IsDeleted)
        {
            return Result<Error>.Failure(RequestErrors.AlreadyDeleted());
        }

        IsDeleted = true;
        AddDomainEvent(new RequestDeletedEvent(ContactEmail));
        return Result<Error>.Success();
    }

    public Result<Error> Restore()
    {
        if (!IsDeleted)
        {
            return Result<Error>.Failure(RequestErrors.NotDeleted());
        }

        IsDeleted = false;
        return Result<Error>.Success();
    }
}