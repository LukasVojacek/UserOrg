using Core.Domain.Request;

namespace Core.UnitTests.Requests;

public sealed class RequestStateTransitionTableTests
{
    private static readonly HashSet<(int From, int To)> AllowedPairs =
    [
        (1, 2), (1, 4), (1, 5),
        (2, 3), (2, 4), (2, 5),
        (3, 4), (3, 5),
    ];

    public static TheoryData<int, int, bool> EveryPair
    {
        get
        {
            var data = new TheoryData<int, int, bool>();
            for (var from = 1; from <= 5; from++)
                for (var to = 1; to <= 5; to++)
                    data.Add(from, to, AllowedPairs.Contains((from, to)));
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Should_AllowExactlyTheEightPairs_When_CheckingEveryTransition(int from, int to, bool expected)
    {
        Assert.Equal(expected, RequestState.Create(from).CanTransitionTo(RequestState.Create(to)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void Should_RejectTheTransition_When_StateIsTheDefaultValue(int to)
    {
        Assert.False(default(RequestState).CanTransitionTo(RequestState.Create(to)));
        Assert.False(RequestState.Create(to).CanTransitionTo(default));
    }

    [Theory]
    [InlineData(1, "Pending")]
    [InlineData(2, "Pending for PI Accept")]
    [InlineData(3, "Pending for HPC Project")]
    [InlineData(4, "Accepted")]
    [InlineData(5, "Rejected")]
    public void Should_ReturnTheHumanReadableText_When_DescribingAState(int value, string expected)
    {
        var state = RequestState.Create(value);

        Assert.Equal(expected, state.GetDescription());
        Assert.Equal(expected, state.ToString());
    }

    [Fact]
    public void Should_DescribeAsUnknown_When_StateIsTheDefaultValue()
    {
        Assert.Equal("Unknown", default(RequestState).GetDescription());
        Assert.Equal("Unknown", default(RequestState).ToString());
    }
}
