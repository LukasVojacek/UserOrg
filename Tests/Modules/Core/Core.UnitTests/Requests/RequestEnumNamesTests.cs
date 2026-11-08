using Core.Domain.Request;

namespace Core.UnitTests.Requests;

public sealed class RequestEnumNamesTests
{
    [Theory]
    [InlineData(1, "Pending")]
    [InlineData(2, "PendingForPIAccept")]
    [InlineData(3, "PendingForHPCProject")]
    [InlineData(4, "Accepted")]
    [InlineData(5, "Rejected")]
    public void RequestState_Name_Should_BeStablePascalCase(int value, string expected)
    {
        Assert.Equal(expected, RequestState.Create(value).Name);
    }

    [Theory]
    [InlineData("Pending", 1)]
    [InlineData("PendingForPIAccept", 2)]
    [InlineData("PendingForHPCProject", 3)]
    [InlineData("Accepted", 4)]
    [InlineData("Rejected", 5)]
    public void RequestState_TryParseName_Should_RoundTrip(string name, int expected)
    {
        Assert.True(RequestState.TryParseName(name, out var state));
        Assert.Equal(expected, state.Value);
        Assert.Equal(name, state.Name);
    }

    [Theory]
    [InlineData("pending")]      // case-sensitive
    [InlineData("Bogus")]
    [InlineData("")]
    public void RequestState_TryParseName_Should_Fail_When_Unknown(string name)
    {
        Assert.False(RequestState.TryParseName(name, out _));
    }

    [Theory]
    [InlineData(0, "Default")]
    [InlineData(1, "SelfOnboarding")]
    [InlineData(2, "ProjectResourceAssignment")]
    [InlineData(3, "HEAppECreation")]
    public void RequestType_Name_Should_BeStablePascalCase(int value, string expected)
    {
        Assert.Equal(expected, RequestType.Create(value).Name);
    }

    [Theory]
    [InlineData("Default", 0)]
    [InlineData("SelfOnboarding", 1)]
    [InlineData("ProjectResourceAssignment", 2)]
    [InlineData("HEAppECreation", 3)]
    public void RequestType_TryParseName_Should_RoundTrip(string name, int expected)
    {
        Assert.True(RequestType.TryParseName(name, out var type));
        Assert.Equal(expected, type.Value);
        Assert.Equal(name, type.Name);
    }

    [Theory]
    [InlineData("selfonboarding")] // case-sensitive
    [InlineData("Bogus")]
    [InlineData("")]
    public void RequestType_TryParseName_Should_Fail_When_Unknown(string name)
    {
        Assert.False(RequestType.TryParseName(name, out _));
    }

    // --- Unified name-table consistency (single source of truth: Name / TryParseName / Create) ---

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RequestState_NameAndTryParseAndCreate_Should_Agree_ForEveryNamedValue(int value)
    {
        var state = RequestState.Create(value);

        Assert.True(RequestState.TryParseName(state.Name, out var parsed));
        Assert.Equal(value, parsed.Value);
        Assert.Equal(state.Name, parsed.Name);
    }

    [Fact]
    public void RequestState_StaticMembers_Should_MatchCreate()
    {
        Assert.Equal(RequestState.Create(1), RequestState.Pending);
        Assert.Equal(RequestState.Create(2), RequestState.PendingForPIAccept);
        Assert.Equal(RequestState.Create(3), RequestState.PendingForHPCProject);
        Assert.Equal(RequestState.Create(4), RequestState.Accepted);
        Assert.Equal(RequestState.Create(5), RequestState.Rejected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RequestType_NameAndTryParseAndCreate_Should_Agree_ForEveryNamedValue(int value)
    {
        var type = RequestType.Create(value);

        Assert.True(RequestType.TryParseName(type.Name, out var parsed));
        Assert.Equal(value, parsed.Value);
        Assert.Equal(type.Name, parsed.Name);
    }

    [Fact]
    public void RequestType_StaticMembers_Should_MatchCreate()
    {
        Assert.Equal(RequestType.Create(0), RequestType.Default);
        Assert.Equal(RequestType.Create(1), RequestType.SelfOnboarding);
        Assert.Equal(RequestType.Create(2), RequestType.ProjectResourceAssignment);
        Assert.Equal(RequestType.Create(3), RequestType.HEAppECreation);
    }

    // valid ints (Create accepts 0-10) but no stable wire name: first unnamed value and upper bound
    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void RequestType_Create_Should_AcceptUnnamedLegacyValues_WithUnknownName(int value)
    {
        var type = RequestType.Create(value);

        Assert.Equal(value, type.Value);
        Assert.Equal("Unknown", type.Name);
        Assert.False(RequestType.TryParseName(type.Name, out _));
    }
}
