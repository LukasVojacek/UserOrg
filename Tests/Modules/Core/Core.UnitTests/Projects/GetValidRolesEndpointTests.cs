using System.Text.Json;

using Core.Features.Project.GetValidRoles;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class GetValidRolesEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(5, "Manager", "Manager", "Project", [], true, 15),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller CallerWithRole(string roleName) => new(
        new IdpId("u"), roleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { [roleName] = new HashSet<ProjectShortName> { ShortName } });

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Projects/PROJ-1/ValidRoles";
        return http;
    }

    private static async Task<IResult> InvokeAsync(Caller caller, bool rolesSucceed = true, HttpContext? http = null, IList<RoleInfo>? catalog = null)
    {
        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rolesSucceed
                ? Result<IList<RoleInfo>, Error>.Success(catalog ?? Roles())
                : Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "unavailable")));

        return await GetValidRolesEndpoint.HandleAsync(
            http ?? HttpContext(), ShortName, caller, roleList.Object, Options.Create(new WellKnownRoles()), CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnRolesBelowOwner_AsCompletePage_When_CallerIsOwner()
    {
        var http = HttpContext();

        var result = await InvokeAsync(CallerWithRole("Owner"), http: http);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetValidRolesEndpoint.Response>>>(result);
        // Strictly below Owner (L20) and at/above Member baseline (L10) → Manager + Member.
        Assert.Equal(2, ok.Value!.Count);
        Assert.Contains(ok.Value, r => r.Name == "Manager");
        Assert.Contains(ok.Value, r => r.Name == "Member");
        Assert.DoesNotContain(ok.Value, r => r.Name == "Owner");
        // Bounded reference list: total count header, no Link.
        Assert.Equal("2", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }

    [Fact]
    public async Task Should_SendRolesWithoutTheirNumericId_When_CallerIsOwner()
    {
        var result = await InvokeAsync(CallerWithRole("Owner"));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetValidRolesEndpoint.Response>>>(result);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.All(wire.RootElement.EnumerateArray(), role =>
            Assert.Equal(["Name", "Description", "Permissions"], role.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_CallerHasNoRoleInProject()
    {
        // Member (L10) — nothing is strictly below the member baseline, so the assignable set is empty.
        var result = await InvokeAsync(CallerWithRole("Member"));

        var ok = Assert.IsType<Ok<IReadOnlyList<GetValidRolesEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
    }

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
        var result = await InvokeAsync(CallerWithRole("Owner"), rolesSucceed: false);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Failed to retrieve roles.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ExcludeRolesBelowCatalogMemberBaseline_When_CatalogNameCasingDiffersFromConfiguredName()
    {
        IList<RoleInfo> catalog =
        [
            new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
            new RoleInfo(3, "MEMBER", "Member", "Project", [], true, 10),
            new RoleInfo(6, "Guest", "Guest", "Project", [], true, 5),
        ];

        var result = await InvokeAsync(CallerWithRole("Owner"), catalog: catalog);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetValidRolesEndpoint.Response>>>(result);
        Assert.Equal(["MEMBER"], ok.Value!.Select(r => r.Name));
    }
}
