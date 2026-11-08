using Core.DataAccess.Queries.Contracts;
using Core.Features.UserInfo;
using Core.Features.UserInfo.GetExtendedUserInfo;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.UnitTests.UserInfo;

public sealed class UserInfoExtendedProjectionTests
{
    private static readonly ProjectShortName Alpha = ProjectShortName.Create("ALPHA");
    private static readonly ProjectShortName Beta = ProjectShortName.Create("BETA");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RoleDefinitions =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["User"] = ["read"],
            ["Owner"] = ["read", "write"],
        };

    private static UserExtendedDetails User(params UserProjectResourceData[] resources) => new(
        IdpId.Create("idp-1"), Email.Create("u@example.com"), UserName.Create("user1"), resources);

    private static UserProjectResourceData Resource(ProjectShortName project, string name, params AssignedLocationData[] locations) =>
        new(project, name, $"{name} description", Start, End, locations);

    [Fact]
    public void Should_EmitOnlyTheDefaultRole_When_TheUserHasNoProjectRoles()
    {
        var response = UserInfoExtendedProjection.BuildExtendedUserInfo(
            User(), "Jane Doe", "User", RoleDefinitions, new Dictionary<string, HashSet<ProjectShortName>>());

        Assert.Equal(new UserInfoExtendedResponse("idp-1", "Jane Doe", "user1", "u@example.com", response.SystemRoles), response);
        var role = Assert.Single(response.SystemRoles);
        Assert.Equal("User", role.RoleName);
        Assert.Null(role.ProjectShortName);
        Assert.Equal(["read"], role.SystemPermissionTypes);
        Assert.Empty(role.ProjectResources);
    }

    [Fact]
    public void Should_AttachEachProjectsResourcesAndLocations_ToEveryProjectRoleRecord()
    {
        var location = new AssignedLocationData(
            "cluster-a", "Cluster A", "https://a.example", true, "HPC", 42, EAllocationUnitType.None,
            [("cores", "128"), ("gpu", "none")]);
        var user = User(
            Resource(Alpha, "res-1", location),
            Resource(Alpha, "res-2"),
            Resource(Beta, "res-3"));

        var response = UserInfoExtendedProjection.BuildExtendedUserInfo(
            user, null, "User", RoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>> { ["Owner"] = [Alpha, ProjectShortName.Create("GAMMA")] });

        Assert.Equal(3, response.SystemRoles.Count);
        var alpha = Assert.Single(response.SystemRoles, r => r.ProjectShortName == "ALPHA");
        Assert.Equal("Owner", alpha.RoleName);
        Assert.Equal("idp-1", alpha.IdpUserId);
        Assert.Equal("u@example.com", alpha.UserEmail);
        Assert.Equal(["read", "write"], alpha.SystemPermissionTypes);
        Assert.Equal(["res-1", "res-2"], alpha.ProjectResources.Select(r => r.ResourceName));
        var first = alpha.ProjectResources[0];
        Assert.Equal("res-1 description", first.Description);
        Assert.Equal(Start, first.StartDate);
        Assert.Equal(End, first.EndDate);
        var cluster = Assert.Single(first.Clusters);
        Assert.Equal(
            new ClusterInfo("cluster-a", "Cluster A", "https://a.example", true, "HPC", 42, EAllocationUnitType.None, cluster.Attributes),
            cluster);
        Assert.Equal([new ClusterAttribute("cores", "128"), new ClusterAttribute("gpu", "none")], cluster.Attributes);
        Assert.Empty(alpha.ProjectResources[1].Clusters);
        var gamma = Assert.Single(response.SystemRoles, r => r.ProjectShortName == "GAMMA");
        Assert.Empty(gamma.ProjectResources);
    }

    [Fact]
    public void Should_Throw_When_TheDefaultRoleIsNotDefined()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => UserInfoExtendedProjection.BuildExtendedUserInfo(
            User(), null, "Missing", RoleDefinitions, new Dictionary<string, HashSet<ProjectShortName>>()));

        Assert.Equal("Default user role 'Missing' not found in Role Service.", ex.Message);
    }

    [Fact]
    public void Should_Throw_When_AProjectRoleIsNotDefined()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => UserInfoExtendedProjection.BuildExtendedUserInfo(
            User(), null, "User", RoleDefinitions,
            new Dictionary<string, HashSet<ProjectShortName>> { ["Ghost"] = [Alpha] }));

        Assert.Equal("Project role 'Ghost' not found in Role Service.", ex.Message);
    }

    [Fact]
    public void Should_ThrowArgumentNull_When_TheUserIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => UserInfoExtendedProjection.BuildExtendedUserInfo(
            null!, null, "User", RoleDefinitions, new Dictionary<string, HashSet<ProjectShortName>>()));
    }
}
