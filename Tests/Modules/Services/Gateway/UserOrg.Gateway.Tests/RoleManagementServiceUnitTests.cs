using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests;

public sealed class RoleManagementServiceUnitTests
{
    [Fact]
    public async Task GetRolesAsync_ReturnsMappedSummaries()
    {
        var mockRoleService = new Mock<IRoleService>();
        var coreRole = new RoleInfo(1, "admin", "Administrator", "Project", Array.Empty<PermissionInfo>(), true, 100);
        mockRoleService
            .Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([coreRole]));

        var sut = new RoleManagementService(mockRoleService.Object, Mock.Of<IPermissionService>(), Mock.Of<IEndpointPermissionService>(), Enumerable.Empty<IForgeImportExport>());

        var result = await sut.GetRolesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var roles = result.Value;
        Assert.Single(roles);
        Assert.Equal("admin", roles.First().Name);
    }

    [Fact]
    public async Task CreateRoleAsync_MapsPermissionsAndReturnsDetail()
    {
        var mockRoleService = new Mock<IRoleService>();

        HashSet<UserOrg.Shared.PermissionKey>? capturedKeys = null;

        var createdRole = new RoleInfo(1, "newrole", "New Role", "Project", Array.Empty<PermissionInfo>(), true, 100);

        mockRoleService
            .Setup(s => s.CreateRole(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<HashSet<UserOrg.Shared.PermissionKey>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, HashSet<UserOrg.Shared.PermissionKey>, CancellationToken>((n, d, c, l, keys, ct) => capturedKeys = keys)
            .ReturnsAsync(Result<RoleInfo, Error>.Success(createdRole));

        var sut = new RoleManagementService(mockRoleService.Object, Mock.Of<IPermissionService>(), Mock.Of<IEndpointPermissionService>(), Enumerable.Empty<IForgeImportExport>());

        var req = new UserOrg.Gateway.Features.RoleManagement.CreateRoleRequest
        {
            Name = "newrole",
            Description = "New Role",
            Permissions = new[] { "perm:read", "perm:write" },
            Category = "Project",
            Level = 100
        };

        var result = await sut.CreateRoleAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedKeys);
        Assert.Contains(capturedKeys!, k => k.Value == "perm:read");
        Assert.Contains(capturedKeys!, k => k.Value == "perm:write");
    }

    internal static readonly string[] permissions = new[] { "perm:read" };

    [Fact]
    public async Task UpdateRolePermissionsAsync_HandlesError()
    {
        var mockRoleService = new Mock<IRoleService>();
        var error = new Error("Err", ErrorSeverity.Error, "desc");

        mockRoleService
            .Setup(s => s.UpdateRolePermissions(It.IsAny<string>(), It.IsAny<HashSet<UserOrg.Shared.PermissionKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Failure(error));

        var sut = new RoleManagementService(mockRoleService.Object, Mock.Of<IPermissionService>(), Mock.Of<IEndpointPermissionService>(), Enumerable.Empty<IForgeImportExport>());

        var res = await sut.UpdateRolePermissionsAsync("role1", permissions, CancellationToken.None);

        Assert.False(res.IsSuccess);
        Assert.Equal(error, res.Error);
    }

    [Fact]
    public async Task SetEndpointPermissionsAsync_CallsEndpointService()
    {
        var mockEndpointService = new Mock<IEndpointPermissionService>();
        EndpointPermissionMap? captured = null;

        mockEndpointService
            .Setup(s => s.SetEndpointPermissionAsync(It.IsAny<EndpointPermissionMap>(), It.IsAny<CancellationToken>()))
            .Callback<EndpointPermissionMap, CancellationToken>((ep, ct) => captured = ep)
            .ReturnsAsync(Result<Error>.Success());

        var sut = new RoleManagementService(Mock.Of<IRoleService>(), Mock.Of<IPermissionService>(), mockEndpointService.Object, Enumerable.Empty<IForgeImportExport>());

        var res = await sut.SetEndpointPermissionsAsync("ep1", permissions, CancellationToken.None);

        Assert.True(res.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("ep1", captured!.EndpointName);
        Assert.Contains("perm:read", captured.Permissions);
    }
}