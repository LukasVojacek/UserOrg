using Core.DataAccess.ResourceRequests;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Pure tests for the projection-layer <see cref="ResourceRequestState"/> value object (ADR-0063 D9).
/// This is a distinct state set from the legacy <c>RequestState</c> and must not reuse its numeric codes.
/// </summary>
public sealed class ResourceRequestStateTests
{
    [Theory]
    [InlineData("PendingPiApproval", false)]
    [InlineData("AwaitingResourceCreation", false)]
    [InlineData("Assigned", true)]
    [InlineData("Rejected", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Failed", true)]
    [InlineData("Expired", true)]
    public void Should_ClassifyTerminality_When_StateInspected(string value, bool expectedTerminal)
    {
        var state = ResourceRequestState.From(value);

        Assert.Equal(expectedTerminal, state.IsTerminal);
        Assert.Equal(value, state.Value);
    }

    [Fact]
    public void Should_ExposeExactlyTheNonTerminalStates_When_FilteredIndexSetRead()
    {
        Assert.Equal(
            ["PendingPiApproval", "AwaitingResourceCreation"],
            ResourceRequestState.NonTerminal.Select(s => s.Value));
    }

    [Fact]
    public void Should_Throw_When_UnknownStateParsed()
    {
        Assert.Throws<ArgumentException>(() => ResourceRequestState.From("Nope"));
    }
}
