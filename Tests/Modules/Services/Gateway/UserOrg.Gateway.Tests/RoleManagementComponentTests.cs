using Bunit;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests;

public sealed class RoleManagementComponentTests
{
    [Fact]
    public void PageRenders_WithDataFromService()
    {
        using var ctx = new Bunit.TestContext();


        var svc = new Mock<IRoleManagementService>();
        svc.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new[] { new RoleSummary { Name = "admin", Description = "Administrator" } }));
        svc.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new[] { new PermissionInfo { Name = "perm1", Description = "Permission" } }));
        svc.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new[] { new EndpointMappingInfo { EndpointName = "ep1", Permissions = new[] { "perm1" } } }));

        ctx.Services.AddSingleton<IRoleManagementService>(svc.Object);
        // RoleManagementPage (rendered by the wrapper) injects ILogger<RoleManagementPage>, provide that.
        ctx.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>>(NullLogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>.Instance);

        // Provide required feature state and circuit guard used by the component.
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState(svc.Object, NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>.Instance));
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard(NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>.Instance));

        using var comp = ctx.Render<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPageWrapper>();

        comp.WaitForAssertion(() => Assert.Contains("admin", comp.Markup, System.StringComparison.OrdinalIgnoreCase));
        var permBtn = comp.FindAll("button").First(b => b.TextContent.Contains("Permissions", System.StringComparison.OrdinalIgnoreCase));
        permBtn.Click();
        comp.WaitForAssertion(() => Assert.Contains("perm1", comp.Markup, System.StringComparison.OrdinalIgnoreCase));
        var epBtn = comp.FindAll("button").First(b => b.TextContent.Contains("Endpoints", System.StringComparison.OrdinalIgnoreCase));
        epBtn.Click();
        comp.WaitForAssertion(() => Assert.Contains("ep1", comp.Markup, System.StringComparison.OrdinalIgnoreCase));
    }
}