using Core.Features.UserInfo;

namespace Core.UnitTests.UserInfo;

public sealed class UserInfoCacheHelpersTests
{
    private static Caller BuildCaller(string role = "User", params (string Role, string[] ShortNames)[] projectRoles)
        => new(
            new IdpId("user-sub"),
            role,
            null,
            projectRoles.ToDictionary(
                p => p.Role,
                p => (IReadOnlySet<ProjectShortName>)p.ShortNames.Select(ProjectShortName.Create).ToHashSet()));

    [Fact]
    public void Should_ReturnSameHash_When_ProjectRolesAreInDifferentOrder()
    {
        var a = BuildCaller("User", ("ProjectManager", ["proj-b", "proj-a"]), ("ProjectUser", ["proj-c"]));
        var b = BuildCaller("User", ("ProjectUser", ["proj-c"]), ("ProjectManager", ["proj-a", "proj-b"]));

        Assert.Equal(UserInfoCacheHelpers.BuildClaimsVersion(a), UserInfoCacheHelpers.BuildClaimsVersion(b));
    }

    [Fact]
    public void Should_ReturnDifferentHash_When_EffectiveRoleChanges()
    {
        var baseline = BuildCaller("User");
        var changed = BuildCaller("Admin");

        Assert.NotEqual(UserInfoCacheHelpers.BuildClaimsVersion(baseline), UserInfoCacheHelpers.BuildClaimsVersion(changed));
    }

    [Fact]
    public void Should_ReturnDifferentHash_When_ProjectScopeChanges()
    {
        var baseline = BuildCaller("User", ("ProjectUser", ["proj-a"]));
        var changed = BuildCaller("User", ("ProjectUser", ["proj-a", "proj-b"]));

        Assert.NotEqual(UserInfoCacheHelpers.BuildClaimsVersion(baseline), UserInfoCacheHelpers.BuildClaimsVersion(changed));
    }

    [Fact]
    public void Should_ReturnLowercaseHex64Chars_ForAnyInput()
    {
        var hash = UserInfoCacheHelpers.BuildClaimsVersion(BuildCaller());

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_CallerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UserInfoCacheHelpers.BuildClaimsVersion(null!));
    }
}
