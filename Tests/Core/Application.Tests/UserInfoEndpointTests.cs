using Forge.Core.Features.UserOrgRoles;

using UserOrganizationManagement.API.API;

public sealed class UserInfoEndpointTests
{
    [Fact]
    public void BuildUserPermissionsCacheVersion_WhenPermissionOrderDiffers_ReturnsSameVersion()
    {
        var first = new[]
        {
            new UserPermissionDto
            {
                IdentityProviderUserId = "user-1",
                ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ShortName = "proj-a",
                SystemRoleId = 2,
                SystemRoleName = "ProjectMember"
            },
            new UserPermissionDto
            {
                IdentityProviderUserId = "user-1",
                ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ShortName = "proj-b",
                SystemRoleId = 1,
                SystemRoleName = "ProjectOwner"
            }
        };

        var second = first.Reverse().ToArray();

        var firstVersion = UserInfoEndpoint.BuildUserPermissionsCacheVersion(first);
        var secondVersion = UserInfoEndpoint.BuildUserPermissionsCacheVersion(second);

        Assert.Equal(firstVersion, secondVersion);
    }

    [Fact]
    public void BuildUserPermissionsCacheVersion_WhenPermissionsChange_ReturnsDifferentVersion()
    {
        var original = new[]
        {
            new UserPermissionDto
            {
                IdentityProviderUserId = "user-1",
                ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ShortName = "proj-a",
                SystemRoleId = 1,
                SystemRoleName = "ProjectMember"
            }
        };

        var updated = new[]
        {
            new UserPermissionDto
            {
                IdentityProviderUserId = "user-1",
                ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ShortName = "proj-a",
                SystemRoleId = 2,
                SystemRoleName = "ProjectOwner"
            }
        };

        var originalVersion = UserInfoEndpoint.BuildUserPermissionsCacheVersion(original);
        var updatedVersion = UserInfoEndpoint.BuildUserPermissionsCacheVersion(updated);

        Assert.NotEqual(originalVersion, updatedVersion);
    }
}
