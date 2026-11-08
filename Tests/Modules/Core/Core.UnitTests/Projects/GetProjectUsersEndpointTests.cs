using Core.DataAccess.Queries.Contracts;
using Core.Features.Project.GetProjectUsers;
using Core.Shared.Paging;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class GetProjectUsersEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static ProjectUserSummary User(int roleId) => new(
        IdpId.Create("target-idp"), Email.Create("t@example.org"), UserName.Create("target"), ProjectRoleId.Create(roleId));

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Projects/PROJ-1/Users";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        List<ProjectUserSummary>? users = null,
        int? totalItems = null,
        bool rolesSucceed = true,
        PageRequest? page = null,
        HttpContext? http = null)
    {
        var items = users ?? [];
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectUsersAsync(It.IsAny<ProjectShortName>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProjectUserSummary>(items, totalItems ?? items.Count));

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rolesSucceed
                ? Result<IList<RoleInfo>, Error>.Success(Roles())
                : Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "unavailable")));

        return await GetProjectUsersEndpoint.HandleAsync(
            http ?? HttpContext(), ShortName, caller, page ?? new PageRequest(null, null), query.Object, roleList.Object, Pagination(), CancellationToken.None);
    }

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    [Fact]
    public async Task Should_ReturnNotFound_With_ProjectNotFoundCode_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnBadGateway_With_UpstreamCode_When_RoleServiceFails()
    {
        var result = await InvokeAsync(CallerInScope(), rolesSucceed: false);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Failed to retrieve roles.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnBareArray_AndTotalCountHeader_When_ProjectHasNoUsers()
    {
        var http = HttpContext();

        var result = await InvokeAsync(CallerInScope(), http: http);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectUsersEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link")); // no pages
    }

    [Fact]
    public async Task Should_MapRoleName_When_RoleIdMatchesKnownRole()
    {
        var result = await InvokeAsync(CallerInScope(), users: [User(roleId: 3)]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectUsersEndpoint.Response>>>(result);
        Assert.Equal("Member", Assert.Single(ok.Value!).RoleName);
    }

    [Fact]
    public async Task Should_FallbackToEmptyRoleName_When_RoleIdUnknown()
    {
        var result = await InvokeAsync(CallerInScope(), users: [User(roleId: 999)]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectUsersEndpoint.Response>>>(result);
        Assert.Equal(string.Empty, Assert.Single(ok.Value!).RoleName);
    }

    [Fact]
    public async Task Should_HonorLimit_AndSetNextLink_When_MoreResultsExist()
    {
        var http = HttpContext();

        var result = await InvokeAsync(CallerInScope(), users: [User(3), User(3)], totalItems: 5, page: new PageRequest(1, 2), http: http)
            ;

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectUsersEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectUsersAsync(It.IsAny<ProjectShortName>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectShortName, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<ProjectUserSummary>([], 0));

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        await GetProjectUsersEndpoint.HandleAsync(
            HttpContext(), ShortName, CallerInScope(), new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, roleList.Object, Pagination(), CancellationToken.None)
            ;

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }
}
