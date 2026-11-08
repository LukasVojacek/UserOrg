using System.Reflection;

using Core.Domain.OnboardingRequest;

namespace Core.UnitTests;

public sealed class OnboardingRequestAggregateTests
{
    private static OnboardingRequest BuildPending(OnboardingRequestSlug? slug = null) => OnboardingRequest.Create(
        ProjectShortName.Create("testproject"),
        IdpId.Create("idp-requester-1"),
        Email.Create("requester@example.com"),
        slug);

    [Fact]
    public void Should_StartPending_When_Created()
    {
        var request = BuildPending();

        Assert.Equal(OnboardingRequestState.Pending.Value, request.State.Value);
    }

    [Fact]
    public void Should_RaiseCreatedEvent_When_Created()
    {
        var request = BuildPending();

        Assert.Single(request.DomainEvents);
        Assert.IsType<OnboardingRequestCreatedEvent>(request.DomainEvents.Single());
    }

    [Fact]
    public void Should_StoreNaturalKeys_When_Created()
    {
        var request = BuildPending();

        Assert.Equal("testproject", request.Project.Value);
        Assert.Equal("idp-requester-1", request.Requester.Value);
        Assert.Equal("requester@example.com", request.ContactEmail.Value);
    }

    [Fact]
    public void Should_StoreSlug_When_Provided()
    {
        var slug = OnboardingRequestSlug.New(Email.Create("requester@example.com"), ProjectShortName.Create("testproject"));

        var request = BuildPending(slug);

        Assert.Equal(slug, request.Slug);
    }

    [Fact]
    public void Should_HaveNullSlug_When_NotProvided()
    {
        Assert.Null(BuildPending().Slug);
    }

    [Fact]
    public void Should_TransitionToAccepted_When_AcceptFromPending()
    {
        var request = BuildPending();

        var result = request.Accept();

        Assert.True(result.IsSuccess);
        Assert.Equal(OnboardingRequestState.Accepted.Value, request.State.Value);
    }

    [Fact]
    public void Should_TransitionToRejected_When_RejectFromPending()
    {
        var request = BuildPending();

        var result = request.Reject();

        Assert.True(result.IsSuccess);
        Assert.Equal(OnboardingRequestState.Rejected.Value, request.State.Value);
    }

    [Fact]
    public void Should_RaiseTransitionedEvent_When_Accepted()
    {
        var request = BuildPending();
        request.ClearDomainEvents();

        request.Accept();

        Assert.IsType<OnboardingRequestStateTransitionedEvent>(request.DomainEvents.Single());
    }

    [Fact]
    public void Should_ReturnInvalidState_When_AcceptingAlreadyAccepted()
    {
        var request = BuildPending();
        request.Accept();

        var result = request.Accept();

        Assert.False(result.IsSuccess);
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, result.Error.Code);
    }

    [Fact]
    public void Should_ReturnInvalidState_When_RejectingAlreadyRejected()
    {
        var request = BuildPending();
        request.Reject();

        var result = request.Reject();

        Assert.False(result.IsSuccess);
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, result.Error.Code);
    }

    [Fact]
    public void Should_ReturnInvalidState_When_RejectingFromAcceptedTerminal()
    {
        var request = BuildPending();
        request.Accept();

        var result = request.Reject();

        Assert.False(result.IsSuccess);
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, result.Error.Code);
    }

    [Fact]
    public void Should_ReturnInvalidState_When_AcceptingFromRejectedTerminal()
    {
        var request = BuildPending();
        request.Reject();

        var result = request.Accept();

        Assert.False(result.IsSuccess);
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, result.Error.Code);
    }

    [Fact]
    public void Should_SoftDelete_When_Deleted()
    {
        var request = BuildPending();

        var result = request.Delete();

        Assert.True(result.IsSuccess);
        Assert.True(request.IsDeleted);
    }

    [Fact]
    public void Should_ExposeNoGuidIds_OnTheModel()
    {
        var props = typeof(OnboardingRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(props, p => p.PropertyType == typeof(Guid));
        Assert.DoesNotContain(props, p => p.Name is "Id" or "ProjectId" or "UserId");
    }
}
