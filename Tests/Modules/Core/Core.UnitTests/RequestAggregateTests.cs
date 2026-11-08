using Core.Domain.Request;

using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests;

public sealed class RequestAggregateTests
{
    private static RequestAggregate BuildPending() => RequestAggregate.Create(
        Email.Create("requester@example.com"),
        ProjectId.New(),
        UserId.New(),
        RequestType.SelfOnboarding);

    [Fact]
    public void Should_StoreSlug_When_Provided()
    {
        var request = RequestAggregate.Create(
            Email.Create("requester@example.com"), ProjectId.New(), UserId.New(),
            RequestType.SelfOnboarding, slug: "req-req-testproject-abc123");

        Assert.Equal("req-req-testproject-abc123", request.Slug);
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
        Assert.Equal(RequestState.Accepted.Value, request.State.Value);
    }

    [Fact]
    public void Should_TransitionToRejected_When_RejectFromPending()
    {
        var request = BuildPending();

        var result = request.Reject();

        Assert.True(result.IsSuccess);
        Assert.Equal(RequestState.Rejected.Value, request.State.Value);
    }

    [Fact]
    public void Should_Fail_When_AcceptingAlreadyAcceptedRequest()
    {
        var request = BuildPending();
        request.Accept();

        var result = request.Accept();

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestErrorCodes.InvalidStateTransition, result.Error.Code);
    }

    [Fact]
    public void Should_Fail_When_RejectingAlreadyRejectedRequest()
    {
        var request = BuildPending();
        request.Reject();

        var result = request.Reject();

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestErrorCodes.InvalidStateTransition, result.Error.Code);
    }

    [Fact]
    public void Should_SoftDeleteAllLinks_When_InvalidateActionLinks()
    {
        var request = BuildPending();
        request.AddActionLink(ActionLinkAction.Accept, "accept-token", DateTimeOffset.UtcNow.AddHours(1));
        request.AddActionLink(ActionLinkAction.Reject, "reject-token", DateTimeOffset.UtcNow.AddHours(1));

        request.InvalidateActionLinks();

        Assert.All(request.ActionLinks, link => Assert.True(link.IsDeleted));
    }
}
