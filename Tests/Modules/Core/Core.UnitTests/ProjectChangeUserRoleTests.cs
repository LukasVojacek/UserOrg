using Core.Domain.Shared;

namespace Core.UnitTests;

public sealed class ProjectChangeUserRoleTests
{
    private static readonly UserId Creator = UserId.New();
    private static readonly ProjectRoleId MemberRole = ProjectRoleId.Create(3);
    private static readonly ProjectRoleId OwnerRole = ProjectRoleId.Create(4);

    private static Project BuildProject(EOperationMode mode = EOperationMode.Editable) =>
        Project.Create(
            ProjectShortName.Create("PROJ-1"),
            ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            Creator,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            ServiceName.Default,
            mode);

    private static User NewUser(string name) =>
        User.CreateUser(Email.Create($"{name}@example.com"), UserName.Create(name), IdpId.Create($"idp-{name}"));

    private static ProjectRoleAssignment OwnerAssignment() =>
        new(OwnerRole, RoleLevel: 20, RequesterLevel: 100, MemberBaselineLevel: 10, OwnerRoleId: OwnerRole);

    private static ProjectRoleAssignment MemberAssignment() =>
        new(MemberRole, RoleLevel: 10, RequesterLevel: 20, MemberBaselineLevel: 10, OwnerRoleId: OwnerRole);

    [Fact]
    public void Should_Throw_When_TargetIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => BuildProject().ChangeUserRole(null!, MemberAssignment()));
    }

    [Fact]
    public void Should_FailWithReadOnlyAndKeepTheRole_When_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);
        var user = NewUser("member");

        var result = project.ChangeUserRole(user, MemberAssignment());

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ReadOnly, result.Error.Code);
        Assert.Null(project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_FailWithReadOnly_When_ProjectIsReadOnlyAndTheLevelsAreAlsoInvalid()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.ChangeUserRole(
            NewUser("member"),
            new ProjectRoleAssignment(OwnerRole, RoleLevel: 100, RequesterLevel: 100, MemberBaselineLevel: 10, OwnerRoleId: OwnerRole));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ReadOnly, result.Error.Code);
    }

    [Fact]
    public void Should_FailWithRoleLevel_When_LevelsAreInvalidAndAnotherOwnerExists()
    {
        var project = BuildProject();
        project.AssignUserWithRole(NewUser("owner"), Creator, OwnerRole);

        var result = project.ChangeUserRole(
            NewUser("candidate"),
            new ProjectRoleAssignment(OwnerRole, RoleLevel: 100, RequesterLevel: 100, MemberBaselineLevel: 10, OwnerRoleId: OwnerRole));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.CannotAssignEqualOrHigherRole, result.Error.Code);
    }

    [Fact]
    public void Should_PromoteAndReturnNoPreviousRole_When_NoOwnerExists()
    {
        var project = BuildProject();
        var user = NewUser("candidate");

        var result = project.ChangeUserRole(user, OwnerAssignment());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(OwnerRole, project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_Succeed_When_TheCurrentOwnerIsAssignedOwnerAgain()
    {
        var project = BuildProject();
        var owner = NewUser("owner");
        project.AssignUserWithRole(owner, Creator, OwnerRole);

        var result = project.ChangeUserRole(owner, OwnerAssignment());

        Assert.True(result.IsSuccess);
        Assert.Equal(OwnerRole, result.Value);
        Assert.Equal(OwnerRole, project.ActiveRoleOf(owner));
    }

    [Fact]
    public void Should_AllowANonOwnerRole_When_AnotherOwnerExists()
    {
        var project = BuildProject();
        project.AssignUserWithRole(NewUser("owner"), Creator, OwnerRole);
        var user = NewUser("member");

        var result = project.ChangeUserRole(user, MemberAssignment());

        Assert.True(result.IsSuccess);
        Assert.Equal(MemberRole, project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_ReactivateAndReturnNoPreviousRole_When_TheUsersRoleWasRemoved()
    {
        var project = BuildProject();
        var user = NewUser("member");
        project.AssignUserWithRole(user, Creator, MemberRole);
        project.UnassignUserWithRole(user, Creator, OwnerRole);

        var result = project.ChangeUserRole(user, OwnerAssignment());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(OwnerRole, project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_PromoteToOwner_When_TheOnlyOwnerRowWasDemoted()
    {
        var project = BuildProject();
        var former = NewUser("former");
        project.AssignUserWithRole(former, Creator, OwnerRole);
        project.ChangeUserRole(former, MemberAssignment());
        var candidate = NewUser("candidate");

        var result = project.ChangeUserRole(candidate, OwnerAssignment());

        Assert.True(result.IsSuccess);
        Assert.Equal(OwnerRole, project.ActiveRoleOf(candidate));
        Assert.Equal(MemberRole, project.ActiveRoleOf(former));
    }
}
