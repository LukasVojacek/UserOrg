using Core.DataAccess.Queries.Contracts;
using Core.Features.UserInfo;

namespace Core.UnitTests.UserInfo;

public sealed class UserInfoProjectionTests
{
    private static readonly UserDetails DefaultUser = new(
        IdentityProviderUserId: new IdpId("user-idp-123"),
        Email: new Email("test@example.com"),
        UserName: new UserName("testuser"));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRoleDefinitions => new Dictionary<string, IReadOnlyList<string>>
    {
        ["User"] = ["perm:read", "perm:list"],
        ["ProjectManager"] = ["perm:read", "perm:write", "perm:manage"],
        ["Admin"] = ["perm:read", "perm:write", "perm:manage", "perm:admin"],
    };

    [Fact]
    public void Should_PlaceDefaultRoleFirst_With_NullProjectShortName()
    {
        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, "Test User", "User", DefaultRoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Null(result.SystemRoles[0].ProjectShortName);
        Assert.Equal("User", result.SystemRoles[0].RoleName);
    }

    [Fact]
    public void Should_UseRoleClaim_As_DefaultRoleName()
    {
        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "Admin", DefaultRoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Equal("Admin", result.SystemRoles[0].RoleName);
    }

    [Fact]
    public void Should_MapDefaultRolePermissions_FromRoleDefinitions()
    {
        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "User", DefaultRoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Equal(DefaultRoleDefinitions["User"], result.SystemRoles[0].SystemPermissionTypes);
    }

    [Fact]
    public void Should_AddOneRecordPerProjectRolePair()
    {
        var projectRoles = new Dictionary<string, HashSet<ProjectShortName>>
        {
            ["ProjectManager"] = [ProjectShortName.Create("proj-a"), ProjectShortName.Create("proj-b")],
        };

        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "User", DefaultRoleDefinitions, projectRoles);

        // 1 default + 2 project records
        Assert.Equal(3, result.SystemRoles.Count);
    }

    [Fact]
    public void Should_MapProjectRolePermissions_FromRoleDefinitions()
    {
        var projectRoles = new Dictionary<string, HashSet<ProjectShortName>>
        {
            ["ProjectManager"] = [ProjectShortName.Create("proj-a")],
        };

        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "User", DefaultRoleDefinitions, projectRoles);

        var projectRecord = result.SystemRoles.Single(r => r.ProjectShortName == "proj-a");
        Assert.Equal(DefaultRoleDefinitions["ProjectManager"], projectRecord.SystemPermissionTypes);
    }

    [Fact]
    public void Should_SetIdpUserIdAndEmail_FromDbDetails_OnEveryRoleRecord()
    {
        var projectRoles = new Dictionary<string, HashSet<ProjectShortName>>
        {
            ["ProjectManager"] = [ProjectShortName.Create("proj-a")],
        };

        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "User", DefaultRoleDefinitions, projectRoles);

        foreach (var role in result.SystemRoles)
        {
            Assert.Equal(DefaultUser.IdentityProviderUserId.Value, role.IdpUserId);
            Assert.Equal(DefaultUser.Email.Value, role.UserEmail);
        }
    }

    [Fact]
    public void Should_MapTopLevelFields_FromDbDetails_And_NameFromClaim()
    {
        const string claimName = "Jméno z Claim";

        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, claimName, "User", DefaultRoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Equal(DefaultUser.IdentityProviderUserId.Value, result.IdentityProviderUserId);
        Assert.Equal(DefaultUser.UserName.Value, result.UserName);
        Assert.Equal(DefaultUser.Email.Value, result.Email);
        Assert.Equal(claimName, result.Name);
    }

    [Fact]
    public void Should_Throw_When_DefaultRoleMissingInRoleDefinitions()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UserInfoProjection.BuildUserInfo(
                DefaultUser, null, "NonExistentRole", DefaultRoleDefinitions,
                new Dictionary<string, HashSet<ProjectShortName>>()));
    }

    [Fact]
    public void Should_Throw_When_ProjectRoleMissingInRoleDefinitions()
    {
        var projectRoles = new Dictionary<string, HashSet<ProjectShortName>>
        {
            ["UnknownRole"] = [ProjectShortName.Create("proj-a")],
        };

        Assert.Throws<InvalidOperationException>(() =>
            UserInfoProjection.BuildUserInfo(
                DefaultUser, null, "User", DefaultRoleDefinitions, projectRoles));
    }

    [Fact]
    public void Should_ReturnOnlyDefaultRole_When_ProjectRolesEmpty()
    {
        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "User", DefaultRoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Single(result.SystemRoles);
    }

    [Fact]
    public void Should_HandleNullName_FromClaim()
    {
        var result = UserInfoProjection.BuildUserInfo(
            DefaultUser, null, "User", DefaultRoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Null(result.Name);
    }
}
