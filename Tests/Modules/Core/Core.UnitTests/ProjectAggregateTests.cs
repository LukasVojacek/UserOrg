using Core.Domain.Shared;

namespace Core.UnitTests;

/// <summary>
/// Pure domain unit tests for the <see cref="Project"/> aggregate.
/// No EF Core, no DB — exercises domain logic only.
/// </summary>
public sealed class ProjectAggregateTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly ProjectName ProjName = ProjectName.Create("Project One");
    private static readonly UserId Creator = UserId.New();
    private static readonly ServiceName Service = ServiceName.Default;

    private static ProjectDomainType DomainType => ProjectDomainType.FromName(DomainTypeName.Create("HPC"));

    private static Project BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = Project.Create(
            ShortName,
            ProjName,
            DomainType,
            Creator,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            Service,
            mode);
        project.ClearDomainEvents();
        return project;
    }

    private static User NewUser(string name = "member1") =>
        User.CreateUser(Email.Create($"{name}@example.com"), UserName.Create(name), IdpId.Create($"idp-{name}"));

    private static readonly ProjectRoleId MemberRole = ProjectRoleId.Create(3);
    private static readonly ProjectRoleId OwnerRole = ProjectRoleId.Create(4);

    private static ProjectRoleAssignment Assignment(
        ProjectRoleId roleId, int roleLevel, int requesterLevel, int memberLevel = 10) =>
        new(roleId, roleLevel, requesterLevel, memberLevel, OwnerRole);

    [Fact]
    public void Should_SetProperties_When_Create()
    {
        var project = Project.Create(
            ShortName, ProjName, DomainType, Creator,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), Service);

        Assert.Equal(ShortName, project.ShortName);
        Assert.Equal(ProjName, project.Name);
        Assert.Equal(Creator, project.CreatedBy);
        Assert.Equal(Service, project.ServiceName);
        Assert.Equal(DomainTypeName.Create("HPC"), project.DomainType);
        Assert.False(project.IsDeleted);
        Assert.Empty(project.Tags);
        Assert.Empty(project.Users);
    }

    [Fact]
    public void Should_Reject_When_CreateWithEndDateBeforeStartDate()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => Project.Create(
            ShortName, ProjName, DomainType, Creator, start, start.AddDays(-1), Service));
    }

    [Fact]
    public void Should_RaiseProjectCreatedEvent_When_Create()
    {
        var project = Project.Create(
            ShortName, ProjName, DomainType, Creator,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), Service);

        var evt = Assert.Single(project.DomainEvents.OfType<ProjectCreatedEvent>());
        Assert.Equal(ShortName, evt.ShortName);
        Assert.Equal(ProjName, evt.Name);
        Assert.Equal(Creator, evt.CreatedBy);
    }

    [Fact]
    public void Should_ChangeName_When_Rename()
    {
        var project = BuildProject();
        var newName = ProjectName.Create("Renamed");

        var result = project.Rename(newName);

        Assert.True(result.IsSuccess);
        Assert.Equal(newName, project.Name);
    }

    [Fact]
    public void Should_Fail_When_Rename_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.Rename(ProjectName.Create("Renamed"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjName, project.Name);
    }

    [Fact]
    public void Should_ChangeDescription_When_UpdateDescription()
    {
        var project = BuildProject();
        var description = Description.Create("new desc");

        var result = project.UpdateDescription(description);

        Assert.True(result.IsSuccess);
        Assert.Equal(description, project.Description);
    }

    [Fact]
    public void Should_Fail_When_UpdateDescription_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.UpdateDescription(Description.Create("new desc"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_SetEmail_When_ChangeContactEmail()
    {
        var project = BuildProject();
        var email = Email.Create("c@example.com");

        var result = project.ChangeContactEmail(email);

        Assert.True(result.IsSuccess);
        Assert.Equal(email, project.Email);
    }

    [Fact]
    public void Should_ClearEmail_When_ChangeContactEmail_WithNull()
    {
        var project = BuildProject();
        project.ChangeContactEmail(Email.Create("c@example.com"));

        var result = project.ChangeContactEmail(null);

        Assert.True(result.IsSuccess);
        Assert.Null(project.Email);
    }

    [Fact]
    public void Should_ChangeDates_When_Reschedule()
    {
        var project = BuildProject();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var end = DateTimeOffset.UtcNow.AddDays(40);

        var result = project.Reschedule(start, end);

        Assert.True(result.IsSuccess);
        Assert.Equal(start, project.StartDate);
        Assert.Equal(end, project.EndDate);
    }

    [Fact]
    public void Should_Fail_When_Reschedule_EndDateBeforeStartDate()
    {
        var project = BuildProject();

        var result = project.Reschedule(DateTimeOffset.UtcNow.AddDays(10), DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Fail_When_Reschedule_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.Reschedule(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_ChangeDomainType_When_ChangeDomainType()
    {
        var project = BuildProject();
        var newDomain = ProjectDomainType.FromName(DomainTypeName.Create("Quantum"));

        var result = project.ChangeDomainType(newDomain);

        Assert.True(result.IsSuccess);
        Assert.Equal(DomainTypeName.Create("Quantum"), project.DomainType);
    }

    [Fact]
    public void Should_Fail_When_ChangeDomainType_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.ChangeDomainType(ProjectDomainType.FromName(DomainTypeName.Create("Quantum")));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_SoftDeleteAndRaiseEvent_When_Delete()
    {
        var project = BuildProject();

        var result = project.Delete();

        Assert.True(result.IsSuccess);
        Assert.True(project.IsDeleted);
        var evt = Assert.Single(project.DomainEvents.OfType<ProjectDeletedEvent>());
        Assert.Equal(ShortName, evt.ShortName);
    }

    [Fact]
    public void Should_Fail_When_Delete_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.Delete();

        Assert.False(result.IsSuccess);
        Assert.False(project.IsDeleted);
    }

    [Fact]
    public void Should_AddUser_When_AssignUser()
    {
        var project = BuildProject();
        var user = NewUser();

        var result = project.AssignUser(user, Creator);

        Assert.True(result.IsSuccess);
        var assigned = Assert.Single(project.Users);
        Assert.Equal(user.UserName, assigned.UserName);
        Assert.Equal(user.Email, assigned.Email);
    }

    [Fact]
    public void Should_Fail_When_AssignUser_AlreadyAssigned()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUser(user, Creator);

        var result = project.AssignUser(user, Creator);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Reactivate_When_AssignUser_AfterUnassign()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUser(user, Creator);
        project.UnassignUser(user, Creator);

        var result = project.AssignUser(user, Creator);

        Assert.True(result.IsSuccess);
        Assert.Single(project.Users);
    }

    [Fact]
    public void Should_RemoveUser_When_UnassignUser()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUser(user, Creator);

        var result = project.UnassignUser(user, Creator);

        Assert.True(result.IsSuccess);
        Assert.Empty(project.Users);
    }

    [Fact]
    public void Should_Fail_When_UnassignUser_NotAssigned()
    {
        var project = BuildProject();

        var result = project.UnassignUser(NewUser(), Creator);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_AddMembershipAndRole_When_AssignUserWithRole()
    {
        var project = BuildProject();
        var user = NewUser();

        var result = project.AssignUserWithRole(user, Creator, MemberRole);

        Assert.True(result.IsSuccess);
        Assert.Single(project.Users);
        Assert.Equal(MemberRole, project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_Fail_When_AssignUserWithRole_AlreadyAssigned()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUserWithRole(user, Creator, MemberRole);

        var result = project.AssignUserWithRole(user, Creator, MemberRole);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.UserAlreadyAssigned, result.Error.Code);
    }

    [Fact]
    public void Should_Fail_When_AssignUserWithRole_ReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);
        var user = NewUser();

        var result = project.AssignUserWithRole(user, Creator, MemberRole);

        Assert.False(result.IsSuccess);
        Assert.Null(project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_RemoveMembershipAndRole_When_UnassignUserWithRole()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUserWithRole(user, Creator, MemberRole);

        var result = project.UnassignUserWithRole(user, Creator, OwnerRole);

        Assert.True(result.IsSuccess);
        Assert.Empty(project.Users);
        Assert.Null(project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_Fail_When_UnassignUserWithRole_TargetIsOwner()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUserWithRole(user, Creator, OwnerRole);

        var result = project.UnassignUserWithRole(user, Creator, OwnerRole);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.OwnerCannotBeUnassigned, result.Error.Code);
        Assert.Single(project.Users);
    }

    [Fact]
    public void Should_Fail_When_UnassignUserWithRole_NotAssigned()
    {
        var project = BuildProject();

        var result = project.UnassignUserWithRole(NewUser(), Creator, OwnerRole);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.UserNotAssigned, result.Error.Code);
    }

    [Fact]
    public void Should_ChangeRole_When_ChangeUserRole_BelowRequesterAndAtBaseline()
    {
        var project = BuildProject();
        var user = NewUser();

        var result = project.ChangeUserRole(user, Assignment(MemberRole, roleLevel: 10, requesterLevel: 20));

        Assert.True(result.IsSuccess);
        Assert.Equal(MemberRole, project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_ReturnPreviousRole_When_ChangeUserRoleSucceeds()
    {
        var project = BuildProject();
        var user = NewUser();
        project.AssignUserWithRole(user, Creator, OwnerRole);

        var result = project.ChangeUserRole(user, Assignment(MemberRole, roleLevel: 10, requesterLevel: 20));

        Assert.True(result.IsSuccess);
        Assert.Equal(OwnerRole, result.Value);
    }

    [Fact]
    public void Should_Fail_When_ChangeUserRole_RoleNotBelowRequester()
    {
        var project = BuildProject();
        var user = NewUser();

        var result = project.ChangeUserRole(user, Assignment(OwnerRole, roleLevel: 20, requesterLevel: 20));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.CannotAssignEqualOrHigherRole, result.Error.Code);
        Assert.Null(project.ActiveRoleOf(user));
    }

    [Fact]
    public void Should_Fail_When_ChangeUserRole_BelowMemberBaseline()
    {
        var project = BuildProject();

        var result = project.ChangeUserRole(
            NewUser(), Assignment(ProjectRoleId.Create(1), roleLevel: 5, requesterLevel: 100, memberLevel: 10));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.CannotAssignEqualOrHigherRole, result.Error.Code);
    }

    [Fact]
    public void Should_Fail_When_ChangeUserRole_SecondOwner()
    {
        var project = BuildProject();
        project.AssignUserWithRole(NewUser("existing-owner"), Creator, OwnerRole);
        var candidate = NewUser("candidate");

        var result = project.ChangeUserRole(
            candidate, new ProjectRoleAssignment(OwnerRole, RoleLevel: 20, RequesterLevel: 100, MemberBaselineLevel: 10, OwnerRoleId: OwnerRole));

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.DuplicateProjectOwner, result.Error.Code);
        Assert.Null(project.ActiveRoleOf(candidate));
    }

    [Fact]
    public void Should_BeIdempotent_When_AssignExistingTag_Twice()
    {
        var project = BuildProject();
        var tag = ProjectTag.Create(TagName.Create("hpc"), UserName.Create("author"));

        project.AssignExistingTag(tag);
        project.AssignExistingTag(tag);

        Assert.Single(project.Tags);
    }

    [Fact]
    public void Should_AssignExistingTag_When_AssignExistingTag()
    {
        var project = BuildProject();
        var tag = ProjectTag.Create(TagName.Create("hpc"), UserName.Create("author"));

        var result = project.AssignExistingTag(tag);

        Assert.True(result.IsSuccess);
        Assert.True(project.HasTag(tag.Name));
    }

    [Fact]
    public void Should_Fail_When_AssignExistingTag_OnReadOnlyProject()
    {
        var project = BuildProject(EOperationMode.ReadOnly);
        var tag = ProjectTag.Create(TagName.Create("hpc"), UserName.Create("author"));

        var result = project.AssignExistingTag(tag);

        Assert.False(result.IsSuccess);
        Assert.Empty(project.Tags);
    }

    [Fact]
    public async Task Should_RemoveTag_When_RemoveTag()
    {
        var project = BuildProject();
        var tag = TagName.Create("hpc");
        project.AssignExistingTag(ProjectTag.Create(tag, UserName.Create("author")));

        var result = project.RemoveTag(tag);

        Assert.True(result.IsSuccess);
        Assert.False(project.HasTag(tag));
        Assert.Empty(project.Tags);
    }

    [Fact]
    public void Should_BeIdempotent_When_RemoveTag_NotAssigned()
    {
        var project = BuildProject();

        var result = project.RemoveTag(TagName.Create("absent"));

        Assert.True(result.IsSuccess);
        Assert.Empty(project.Tags);
    }

    [Fact]
    public void Should_Fail_When_RemoveTag_OnReadOnlyProject()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.RemoveTag(TagName.Create("hpc"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_SetEndDateToMaxValue_When_SetIndefiniteValidity()
    {
        var project = BuildProject();

        var result = project.SetIndefiniteValidity(TimeProvider.System);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.MaxValue, project.EndDate);
    }

    [Fact]
    public void Should_Fail_When_SetIndefiniteValidity_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.SetIndefiniteValidity(TimeProvider.System);

        Assert.False(result.IsSuccess);
    }

    private static ProjectResource NewResource(string name = "res-1")
    {
        var provider = Core.Domain.Provider.Provider.Create(ProviderName.From("acme"), countryId: 1);
        return ProjectResource.Create(
            ResourceName.Create(name), provider, UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30));
    }

    [Fact]
    public void Should_AddToResources_When_AssignResource()
    {
        var project = BuildProject();
        var resource = NewResource("gpu-res");

        var result = project.AssignResource(resource);

        Assert.True(result.IsSuccess);
        Assert.Contains(project.Resources, r => r.Name == ResourceName.Create("gpu-res"));
    }

    [Fact]
    public void Should_ExposeProviderName_When_ResourceAssigned()
    {
        // ADR-0041 §1: the Resources read model carries the owning provider's natural key, not just the name.
        var project = BuildProject();
        var resource = NewResource("gpu-res"); // NewResource owns provider "acme"

        Assert.True(project.AssignResource(resource).IsSuccess);

        var detail = Assert.Single(project.Resources, r => r.Name == ResourceName.Create("gpu-res"));
        Assert.Equal(ProviderName.From("acme"), detail.ProviderName);
    }

    [Fact]
    public void Should_RaiseAssignmentChangedEvent_When_AssignResource()
    {
        var project = BuildProject();
        var resource = NewResource("gpu-res");

        project.AssignResource(resource);

        var evt = Assert.Single(project.DomainEvents.OfType<ProjectResourceAssignmentChangedEvent>());
        Assert.Equal(ShortName, evt.ShortName);
        Assert.Equal(ResourceName.Create("gpu-res"), evt.ResourceName);
    }

    [Fact]
    public void Should_FailWithAlreadyAssigned_When_AssignActiveResourceTwice()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);

        var result = project.AssignResource(resource);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ResourceAlreadyAssigned, result.Error.Code);
    }

    [Fact]
    public void Should_ReactivateAndNotDuplicate_When_AssignSoftDeletedResourceLink()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);
        Assert.True(project.RemoveResource(resource).IsSuccess);

        var result = project.AssignResource(resource);

        Assert.True(result.IsSuccess);
        // Reactivated, not duplicated: exactly one active link to the resource.
        Assert.Single(project.Resources, r => r.Name == resource.ResourceName);
    }

    [Fact]
    public void Should_RemoveFromResources_When_RemoveResource()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);

        var result = project.RemoveResource(resource);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(project.Resources, r => r.Name == resource.ResourceName);
    }

    [Fact]
    public void Should_RaiseAssignmentChangedEvent_When_RemoveResource()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);
        project.ClearDomainEvents();

        project.RemoveResource(resource);

        var evt = Assert.Single(project.DomainEvents.OfType<ProjectResourceAssignmentChangedEvent>());
        Assert.Equal(resource.ResourceName, evt.ResourceName);
    }

    [Fact]
    public void Should_FailWithNotAssigned_When_RemoveResourceNeverAssigned()
    {
        var project = BuildProject();

        var result = project.RemoveResource(NewResource());

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ResourceNotAssigned, result.Error.Code);
    }

    [Fact]
    public void Should_FailWithNotAssigned_When_RemoveResourceAlreadyRemoved()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);
        Assert.True(project.RemoveResource(resource).IsSuccess);

        var result = project.RemoveResource(resource);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ResourceNotAssigned, result.Error.Code);
    }

    [Fact]
    public void Should_Fail_When_AssignResource_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.AssignResource(NewResource());

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ReadOnly, result.Error.Code);
    }

    [Fact]
    public void Should_Fail_When_RemoveResource_ProjectIsReadOnly()
    {
        var project = BuildProject(EOperationMode.ReadOnly);

        var result = project.RemoveResource(NewResource());

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectErrorCodes.ReadOnly, result.Error.Code);
    }
}
