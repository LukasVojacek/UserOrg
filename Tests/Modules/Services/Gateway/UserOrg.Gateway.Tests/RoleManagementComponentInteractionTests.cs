using Bunit;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests;

public sealed class RoleManagementComponentInteractionTests
{
    [Fact]
    public async Task CreateRole_ButtonInvokesService()
    {
        using var ctx = new Bunit.TestContext();

        var svc = new Mock<IRoleManagementService>();

        svc.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new[] { new RoleSummary { Name = "admin", Description = "Administrator" } }));

        svc.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new[] { new PermissionInfo { Name = "perm1", Description = "Permission" } }));

        svc.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new[] { new EndpointMappingInfo { EndpointName = "ep1", Permissions = new[] { "perm1" } } }));

        var created = new RoleDetail { Name = "newrole", Description = "New Role", Category = "Default", Level = 0, Permissions = new[] { "perm1" } };
        svc.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(created));

        svc.Setup(s => s.GetRoleAsync("newrole", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(created));

        ctx.Services.AddSingleton<IRoleManagementService>(svc.Object);
        // Provide the feature state and circuit guard used by the component, and the logger the real component expects
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState(svc.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>.Instance));
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard(Microsoft.Extensions.Logging.Abstractions.NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>.Instance));
        ctx.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>>(NullLogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>.Instance);

        using var comp = ctx.Render<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPageWrapper>();

        comp.WaitForAssertion(() => Assert.Contains("admin", comp.Markup, System.StringComparison.OrdinalIgnoreCase));
        // Ensure the inputs are present and use Input (triggers oninput) for text fields
        comp.WaitForAssertion(() => Assert.NotNull(comp.Find("input.rm-input")));
        // Call the wrapper's test helper directly to invoke the service (avoids bUnit event dispatch issues)
        await comp.Instance.TestCreate();

        svc.Verify(s => s.CreateRoleAsync(It.Is<CreateRoleRequest>(r => r.Name == "newrole"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRolePermissions_TriggersServiceCall()
    {
        using var ctx = new Bunit.TestContext();

        var svc = new Mock<IRoleManagementService>();

        svc.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(new[] { new RoleSummary { Name = "admin", Description = "Administrator" } }));

        svc.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new[] { new PermissionInfo { Name = "perm1", Description = "Permission" } }));

        var roleDetail = new RoleDetail { Name = "admin", Description = "Administrator", Category = "Default", Level = 0, Permissions = new string[0] };
        svc.Setup(s => s.GetRoleAsync("admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RoleDetail, Error>.Success(roleDetail));

        svc.Setup(s => s.UpdateRolePermissionsAsync("admin", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        svc.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(Array.Empty<EndpointMappingInfo>()));

        ctx.Services.AddSingleton<IRoleManagementService>(svc.Object);
        // Provide the feature state and circuit guard used by the component, and the logger the real component expects
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState(svc.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>.Instance));
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard(Microsoft.Extensions.Logging.Abstractions.NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>.Instance));
        ctx.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>>(NullLogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>.Instance);

        using var comp = ctx.Render<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPageWrapper>();

        comp.WaitForAssertion(() => Assert.Contains("admin", comp.Markup, System.StringComparison.OrdinalIgnoreCase));

        await comp.Instance.TestUpdatePerms();

        svc.Verify(s => s.UpdateRolePermissionsAsync("admin", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEndpointPermissions_CallsService()
    {
        using var ctx = new Bunit.TestContext();

        var svc = new Mock<IRoleManagementService>();

        svc.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(Array.Empty<RoleSummary>()));

        svc.Setup(s => s.GetPermissionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(new[] { new PermissionInfo { Name = "perm1", Description = "Permission" } }));

        svc.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new[] { new EndpointMappingInfo { EndpointName = "ep1", Permissions = new[] { "perm1" } } }));

        svc.Setup(s => s.SetEndpointPermissionsAsync("ep1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Success(true));

        ctx.Services.AddSingleton<IRoleManagementService>(svc.Object);
        // Provide the logger the real component expects
        ctx.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>>(NullLogger<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPage>.Instance);
        // Provide the feature state and circuit guard used by the component
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState(svc.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementState>.Instance));
        ctx.Services.AddSingleton<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>(
            new UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard(Microsoft.Extensions.Logging.Abstractions.NullLogger<UserOrg.Gateway.Features.RoleManagement.Services.RoleManagementCircuitGuard>.Instance));

        using var comp = ctx.Render<UserOrg.Gateway.Features.RoleManagement.Pages.RoleManagementPageWrapper>();
        var epBtn = comp.FindAll("button").First(b => b.TextContent.Contains("Endpoints", System.StringComparison.OrdinalIgnoreCase));
        epBtn.Click();
        comp.WaitForAssertion(() => Assert.Contains("ep1", comp.Markup, System.StringComparison.OrdinalIgnoreCase));

        await comp.Instance.TestUpdateEndpoint();

        svc.Verify(s => s.SetEndpointPermissionsAsync("ep1", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}