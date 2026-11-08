using System.Security.Claims;
using System.Text.Json;

using Core.DataAccess.Queries.Contracts;

using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using UserOrg.Ddi.Features.Service;
using UserOrg.Shared;

using Xunit;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace UserOrg.Ddi.Tests;

public sealed class GetSyncDetailsEndpointTests
{
    private const int RoleId = 7;

    private static ClaimsPrincipal Principal(string? providerId) =>
        new(new ClaimsIdentity(
            providerId is null ? [] : [new Claim(AuthConstants.ProviderIdClaimType, providerId)]));

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(
            RoleId, "ProjectMember", "Project member", "Project",
            [new PermissionInfo(new PermissionKey("perm:projects:read"), "read")],
            true, 10)
    ];

    [Fact]
    public async Task Should_Map_BothGroups_With_RoleNameAndPermissions_When_Query_Returns_Data()
    {
        var providerUser = new SyncUserView(
            Guid.NewGuid(), "provider-user", "provider-user", "pu@example.com", false,
            [new SyncUserRoleView("prov-proj", "pu@example.com", RoleId,
                [new SyncResourceView("gpu-res", "desc", null, null, Core.Domain.Provider.EProjectResourceState.Published,
                    [new SyncLocationView("loc", "loc-desc", "https://loc", true, "Cluster", 10, EAllocationUnitType.NodeHours,
                        [new SyncLocationValueView("CORES", "128")])])])]);

        var outsider = new SyncUserView(
            Guid.NewGuid(), "outsider", "outsider", "out@example.com", false,
            [new SyncUserRoleView("a-proj", "out@example.com", RoleId, [])]);

        var query = new Mock<ISyncDetailsQuery>();
        query.Setup(q => q.GetSyncDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([providerUser, outsider]);

        var roleService = new Mock<IRoleListService>();
        roleService.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        var result = await GetSyncDetailsEndpoint.HandleAsync(
            Principal(Guid.NewGuid().ToString()), query.Object, roleService.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<List<GetSyncDetailsEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);

        var pu = Assert.Single(ok.Value, u => u.KeycloakSid == "provider-user");
        var role = Assert.Single(pu.SystemRoles);
        Assert.Equal("prov-proj", role.ProjectShortName);
        Assert.Equal("ProjectMember", role.RoleName);
        Assert.Equal("perm:projects:read", Assert.Single(role.SystemPermissionTypes));

        var resource = Assert.Single(role.ProjectResources);
        Assert.Equal("gpu-res", resource.Name);
        var location = Assert.Single(resource.Locations);
        Assert.Equal("loc-desc", location.Descritpion);
        Assert.Equal(EAllocationUnitType.NodeHours, location.AllocationUnitType);

        Assert.Single(ok.Value, u => u.KeycloakSid == "outsider");
    }

    [Fact]
    public void Should_Serialize_LocationDescription_With_Misspelled_Key_When_Responding()
    {
        var location = new GetSyncDetailsEndpoint.LocationModel(
            "loc", "the-description", "https://loc", false, "Cluster", null, EAllocationUnitType.NodeHours, []);

        var json = JsonSerializer.Serialize(location, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Legacy misspells the key — parity requires "descritpion", not "description".
        Assert.Contains("\"descritpion\":\"the-description\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"description\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Return_BadRequest_When_ProviderId_Claim_Missing()
    {
        var query = new Mock<ISyncDetailsQuery>();
        var roleService = new Mock<IRoleListService>();

        var result = await GetSyncDetailsEndpoint.HandleAsync(
            Principal(null), query.Object, roleService.Object, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        query.Verify(q => q.GetSyncDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Return_BadRequest_When_ProviderId_Claim_Not_A_Guid()
    {
        var query = new Mock<ISyncDetailsQuery>();
        var roleService = new Mock<IRoleListService>();

        var result = await GetSyncDetailsEndpoint.HandleAsync(
            Principal("not-a-guid"), query.Object, roleService.Object, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        query.Verify(q => q.GetSyncDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
