using Forge.Core.Features.Role.Services;

using UserOrganizationManagement.API.API;

public sealed class ValidRolesFilterTests
{
    private const string ProjectCategory = "Project";

    private static RoleInfo CreateRole(int id, string name, int level, string category = "Project") =>
        new(id, name, $"{name} description", category, [], true, level);

    private static readonly IList<RoleInfo> AllRoles =
    [
        CreateRole(1, "User",                 level: 1,   category: "System"),
        CreateRole(2, "Member",               level: 10,  category: ProjectCategory),
        CreateRole(3, "DataPublisher",        level: 10,  category: ProjectCategory),
        CreateRole(4, "JobscriptOperator",    level: 10,  category: ProjectCategory),
        CreateRole(5, "Manager",              level: 15,  category: ProjectCategory),
        CreateRole(6, "Owner",                level: 20,  category: ProjectCategory),
        CreateRole(7, "ProviderAdmin",        level: 90,  category: "System"),
        CreateRole(8, "Admin",                level: 100, category: "System"),
    ];

    [Fact(DisplayName = "Owner gets subordinate project roles, not Owner itself")]
    public void Should_ReturnSubordinateRoles_When_UserIsOwner()
    {
        var result = ValidRolesFilter
            .GetValidRoles(AllRoles, ["Owner"], "Member")
            .Select(r => r.Name)
            .Order()
            .ToList();

        Assert.DoesNotContain("Owner", result);
        Assert.Contains("Manager", result);
        Assert.Contains("Member", result);
        Assert.Contains("DataPublisher", result);
        Assert.Contains("JobscriptOperator", result);
    }

    [Fact(DisplayName = "Owner does not get system roles (Admin, ProviderAdmin, User)")]
    public void Should_ExcludeSystemRoles_When_UserIsOwner()
    {
        var result = ValidRolesFilter
            .GetValidRoles(AllRoles, ["Owner"], "Member")
            .Select(r => r.Name)
            .ToList();

        Assert.DoesNotContain("Admin", result);
        Assert.DoesNotContain("ProviderAdmin", result);
        Assert.DoesNotContain("User", result);
    }

    [Fact(DisplayName = "Manager gets only roles below Manager level, not Owner or Manager itself")]
    public void Should_ReturnOnlyLowerLevelRoles_When_UserIsManager()
    {
        var result = ValidRolesFilter
            .GetValidRoles(AllRoles, ["Manager"], "Member")
            .Select(r => r.Name)
            .ToList();

        Assert.DoesNotContain("Owner", result);
        Assert.DoesNotContain("Manager", result);
        Assert.Contains("Member", result);
        Assert.Contains("DataPublisher", result);
    }

    [Fact(DisplayName = "Member gets empty list because no assignable roles are below Member level")]
    public void Should_ReturnEmptyList_When_UserIsMember()
    {
        var result = ValidRolesFilter
            .GetValidRoles(AllRoles, ["Member"], "Member")
            .ToList();

        Assert.Empty(result);
    }

    [Fact(DisplayName = "User with no role in the project gets empty list")]
    public void Should_ReturnEmptyList_When_UserHasNoRoleInProject()
    {
        var result = ValidRolesFilter
            .GetValidRoles(AllRoles, [], "Member")
            .ToList();

        Assert.Empty(result);
    }

    [Fact(DisplayName = "Owner with multiple roles returns union of subordinate roles (highest level wins)")]
    public void Should_UseHighestLevelRole_When_UserHasMultipleRoles()
    {
        // If a user somehow holds both Owner and Manager, Owner (level 20) is the ceiling.
        var result = ValidRolesFilter
            .GetValidRoles(AllRoles, ["Owner", "Manager"], "Member")
            .Select(r => r.Name)
            .ToList();

        // Manager (15) should appear since it's below Owner (20)
        Assert.Contains("Manager", result);
        Assert.DoesNotContain("Owner", result);
    }
}
