using System.Text.Json;

using Core.DataAccess.Queries.Contracts;
using Core.Features.User.GetUserPreferences;
using Core.Features.User.UpdateUserPreferences;
using Core.Shared;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.Requests;

public sealed class UpdateUserPreferencesEndpointTests
{
    private static Caller AnyCaller() => new(
        new IdpId("idp-1"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static UpdateUserPreferencesEndpoint.Request Prefs(string json = """{"theme":"dark"}""") =>
        new(JsonDocument.Parse(json).RootElement);

    private static async Task<IResult> InvokeAsync(
        UserId? userId, Domain.User.User? user, UpdateUserPreferencesEndpoint.Request? request = null)
    {
        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(user));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(userId);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await UpdateUserPreferencesEndpoint.HandleAsync(
            request ?? Prefs(), AnyCaller(), users.Object, directory.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<UpdateUserPreferencesEndpoint.AuditCategory>(), NullLogger<UpdateUserPreferencesEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    /// <summary>
    /// Runs the real GET handler so the round-trip test consumes a <see cref="JsonElement"/> whose backing
    /// <see cref="JsonDocument"/> the handler has already disposed — a missing <c>Clone()</c> must fail here.
    /// </summary>
    private static async Task<GetUserPreferencesEndpoint.Response> GetStoredPreferencesAsync(string stored)
    {
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.GetUserPreferencesAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await GetUserPreferencesEndpoint.HandleAsync(
            AnyCaller(), query.Object, NullLoggerFactory.Instance, CancellationToken.None);

        return Assert.IsType<Ok<GetUserPreferencesEndpoint.Response>>(result).Value!;
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_PreferencesUpdated()
    {
        var user = Domain.User.User.CreateUser(Email.Create("u@example.com"), UserName.Create("user"), IdpId.Create("idp-1"));

        var result = await InvokeAsync(UserId.New(), user);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotProvisioned_When_DirectoryHasNoUserId()
    {
        var result = await InvokeAsync(userId: null, user: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("caller.not_provisioned", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotProvisioned_When_UserAggregateMissing()
    {
        var result = await InvokeAsync(UserId.New(), user: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("caller.not_provisioned", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_PreferencesMissing()
    {
        var user = Domain.User.User.CreateUser(Email.Create("u@example.com"), UserName.Create("user"), IdpId.Create("idp-1"));

        var result = await InvokeAsync(UserId.New(), user, request: new UpdateUserPreferencesEndpoint.Request(default));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.Required, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_StoreSamePreferences_When_FedFromGetResponse()
    {
        var user = Domain.User.User.CreateUser(Email.Create("u@example.com"), UserName.Create("user"), IdpId.Create("idp-1"));
        var getResponse = await GetStoredPreferencesAsync("""{"theme":"dark"}""");

        var result = await InvokeAsync(
            UserId.New(), user, request: new UpdateUserPreferencesEndpoint.Request(getResponse.Preferences));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal("dark", user.UserPreferences.RootElement.GetProperty("theme").GetString());
    }
}
