using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using RemI.Features.Projects;
using RemI.Features.Users;
using RemI.HostContracts;

namespace RemI.Adapter.Tests;

public sealed class EndpointTests
{
    [Fact]
    public async Task GetUsers_Returns_Ok_With_The_Service_Rows()
    {
        UserResponse[] rows = [new("id-1", "sub-1", "a@b.cz", "alice", ["PRJ1"], null)];
        var service = new Mock<IUserorgService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UserResponse>, Error>.Success(rows, State.Ok));

        var result = await GetUsersEndpoint.HandleAsync(service.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<UserResponse>>>(result);
        Assert.Equal("sub-1", Assert.Single(ok.Value!).IdpUserId);
    }

    [Fact]
    public async Task GetUsers_Returns_Ok_With_An_Empty_List_When_There_Are_No_Rows()
    {
        var service = new Mock<IUserorgService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UserResponse>, Error>.Success([], State.Ok));

        var result = await GetUsersEndpoint.HandleAsync(service.Object, CancellationToken.None);

        Assert.Empty(Assert.IsType<Ok<IReadOnlyList<UserResponse>>>(result).Value!);
    }

    [Fact]
    public async Task GetUsers_Returns_500_When_The_Service_Fails()
    {
        var service = new Mock<IUserorgService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UserResponse>, Error>.Failure(
                new ErrorBuilder().WithDescription("boom").Build(), State.CriticalError));

        var result = await GetUsersEndpoint.HandleAsync(service.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetProjects_Returns_Ok_With_The_Service_Rows()
    {
        ProjectResponse[] rows =
        [
            new("PRJ1", "Project One", null, null, "Editable", "svc", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        ];
        var service = new Mock<IUserorgService>();
        service.Setup(s => s.GetProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<ProjectResponse>, Error>.Success(rows, State.Ok));

        var result = await GetProjectsEndpoint.HandleAsync(service.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<ProjectResponse>>>(result);
        Assert.Equal("PRJ1", Assert.Single(ok.Value!).ShortName);
    }

    [Fact]
    public async Task GetProjects_Returns_500_When_The_Service_Fails()
    {
        var service = new Mock<IUserorgService>();
        service.Setup(s => s.GetProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<ProjectResponse>, Error>.Failure(
                new ErrorBuilder().WithDescription("boom").Build(), State.CriticalError));

        var result = await GetProjectsEndpoint.HandleAsync(service.Object, CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }
}