using System.Security.Claims;
using System.Text.Json;

using Core.DataAccess.Queries.Contracts;
using Core.Features.User.GetUserPreferences;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.Users;

public sealed class GetUserPreferencesEndpointTests
{
    private static Caller AnyCaller() => new(
        new IdpId("caller-sub"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static async Task<IResult> InvokeAsync(Caller caller, string preferences = "{}")
    {
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUserPreferencesAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(preferences);

        return await GetUserPreferencesEndpoint.HandleAsync(caller, query.Object, NullLoggerFactory.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnNull_When_SubClaimMissing()
    {
        // No manual 401 in the handler any more — an absent sub fails Caller binding (standard challenge).
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller);
    }

    [Fact]
    public async Task Should_ReturnOk_With_PreferencesEnvelope_When_Authenticated()
    {
        var result = await InvokeAsync(AnyCaller(), preferences: "{\"theme\":\"dark\"}");

        var ok = Assert.IsType<Ok<GetUserPreferencesEndpoint.Response>>(result);
        Assert.Equal("dark", ok.Value!.Preferences.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task Should_ReturnEmptyObject_When_StoredPreferencesAreInvalidJson()
    {
        var result = await InvokeAsync(AnyCaller(), preferences: "not json");

        var ok = Assert.IsType<Ok<GetUserPreferencesEndpoint.Response>>(result);
        Assert.Equal(JsonValueKind.Object, ok.Value!.Preferences.ValueKind);
        Assert.Empty(ok.Value.Preferences.EnumerateObject());
    }
}
