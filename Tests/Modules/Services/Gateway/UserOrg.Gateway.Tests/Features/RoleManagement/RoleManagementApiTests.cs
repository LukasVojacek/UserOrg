using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

public sealed class RoleManagementApiTests
{
    [Fact]
    public async Task BuildRbacAsync_MapsEndpointsToRoles()
    {
        var mockService = new Mock<IRoleManagementService>();

        var endpoint = new EndpointMappingInfo
        {
            EndpointName = "ep1",
            PathTemplate = "/api/values",
            Permissions = new[] { "perm:read" }
        };

        mockService
            .Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new[] { endpoint }));

        var roleSummary = new RoleSummary { Name = "role1" };
        mockService
            .Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new[] { roleSummary }));

        var roleDetail = new RoleDetail { Name = "role1", Permissions = new[] { "perm:read" }, Category = "Project", Level = 1 };
        mockService
            .Setup(s => s.GetRoleAsync(It.Is<string>(r => r == "role1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(roleDetail));

        var result = await RoleManagementApi.BuildRbacAsync(mockService.Object, CancellationToken.None);

        Assert.Single(result);
        var entry = result.First();
        Assert.Equal("/api/values", entry.Endpoint);
        Assert.Single(entry.Roles);
        Assert.Equal("role1", entry.Roles[0]);
    }

    [Fact]
    public async Task Should_Throw_When_TheEndpointMappingsCannotBeRead()
    {
        var service = new Mock<IRoleManagementService>();
        service.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Failure(Failure()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Equal("Failed to retrieve endpoint mappings", exception.Message);
        service.Verify(s => s.GetRolesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Throw_When_TheRolesCannotBeRead()
    {
        var service = Service([Endpoint("ep", "/api/x", "perm:a")]);
        service.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Failure(Failure()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Equal("Failed to retrieve roles", exception.Message);
    }

    [Fact]
    public async Task Should_LeaveOutARole_When_ItsDetailCannotBeRead()
    {
        var service = Service([Endpoint("ep", "/api/x", "perm:a")], Role("readable", "perm:a"), Role("unreadable", "perm:a"));
        service.Setup(s => s.GetRoleAsync("unreadable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Failure(Failure()));

        var entry = Assert.Single(await RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Equal(["readable"], entry.Roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Should_KeyTheEntryByTheEndpointName_When_ThePathTemplateIsBlank(string? pathTemplate)
    {
        var service = Service([Endpoint("GetThings", pathTemplate, "perm:a")]);

        var entry = Assert.Single(await RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Equal("GetThings", entry.Endpoint);
    }

    [Fact]
    public async Task Should_CarryTheMethodAndPermissions_When_BuildingAnEntry()
    {
        var service = Service([new EndpointMappingInfo { EndpointName = "ep", PathTemplate = "/api/x", HttpMethod = "DELETE", Permissions = ["perm:a", "perm:b"] }]);

        var entry = Assert.Single(await RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Equal("DELETE", entry.HttpMethod);
        Assert.Equal(["perm:a", "perm:b"], entry.Permissions);
        Assert.Empty(entry.Roles);
    }

    [Fact]
    public async Task Should_TreatNullPermissionsAsNone_When_AnEndpointOrRoleHasNone()
    {
        var service = Service(
            [new EndpointMappingInfo { EndpointName = "ep", PathTemplate = "/api/x", Permissions = null! }],
            new RoleDetail { Name = "no-perms", Category = "System", Level = 0, Permissions = null! });

        var entry = Assert.Single(await RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Empty(entry.Permissions);
        Assert.Empty(entry.Roles);
    }

    [Fact]
    public async Task Should_AllowEveryRoleSharingAPermissionIgnoringCase_When_MatchingRolesToAnEndpoint()
    {
        var service = Service(
            [Endpoint("ep", "/api/x", "Perm:Read")],
            Role("reader", "perm:read"),
            Role("writer", "perm:write"),
            Role("both", "perm:write", "PERM:READ"));

        var entry = Assert.Single(await RoleManagementApi.BuildRbacAsync(service.Object, TestContext.Current.CancellationToken));

        Assert.Equal(["reader", "both"], entry.Roles);
    }

    [Fact]
    public async Task Should_AnswerOkWithTheMatrix_When_TheRbacEndpointSucceeds()
    {
        var service = Service([Endpoint("ep", "/api/x", "perm:a")], Role("r", "perm:a"));

        var result = await RoleManagementApi.GetRbacEndpoint(service.Object, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<RbacEntry>>>(result);
        var entry = Assert.Single(ok.Value!);
        Assert.Equal("/api/x", entry.Endpoint);
    }

    [Fact]
    public async Task Should_AnswerAProblemWithTheMessage_When_TheRbacEndpointFails()
    {
        var service = new Mock<IRoleManagementService>();
        service.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Failure(Failure()));

        var result = await RoleManagementApi.GetRbacEndpoint(service.Object, TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.Equal("Failed to build RBAC matrix", problem.ProblemDetails.Title);
        Assert.Equal("Failed to retrieve endpoint mappings", problem.ProblemDetails.Detail);
    }

    private static Error Failure() => new("load.failed", ErrorSeverity.Error, "load failed");

    private static EndpointMappingInfo Endpoint(string name, string? pathTemplate, params string[] permissions) =>
        new() { EndpointName = name, PathTemplate = pathTemplate, Permissions = permissions };

    private static RoleDetail Role(string name, params string[] permissions) =>
        new() { Name = name, Category = "Project", Level = 1, Permissions = permissions };

    private static Mock<IRoleManagementService> Service(EndpointMappingInfo[] endpoints, params RoleDetail[] roles)
    {
        var service = new Mock<IRoleManagementService>();
        service.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(endpoints));
        service.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success([.. roles.Select(role => new RoleSummary { Name = role.Name })]));
        foreach (var role in roles)
        {
            service.Setup(s => s.GetRoleAsync(role.Name, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<RoleDetail, Error>.Success(role));
        }

        return service;
    }
}
