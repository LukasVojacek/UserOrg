using Core.Domain;

namespace Core.UnitTests;

/// <summary>
/// Pure domain unit tests for the <see cref="User"/> aggregate.
/// No EF Core, no DB — exercises domain logic only.
///
/// The repository hydration path (PromoteUserToRole + InitializeRoleList) is replicated
/// in <see cref="BuildHydratedUser"/> to allow testing role-dependent operations.
/// </summary>
public sealed class UserAggregateTests
{

    /// <summary>
    /// Role map with the three well-known system roles (mirrors TestRoleService).
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> DefaultRoleMap = new Dictionary<int, string>
    {
        { 1, "User" },
        { 2, "Admin" },
        { 7, "ProviderAdmin" }
    };

    private static readonly WellKnownRoles DefaultWellKnownRoles = new();

    private static readonly Email TestEmail = Email.Create("test@example.com");
    private static readonly UserName TestUserName = UserName.Create("testuser");
    private static readonly IdpId TestIdpId = IdpId.Create("idp-test-123");
    private static readonly ProviderId ProviderId1 = ProviderId.New();
    private static readonly ProviderId ProviderId2 = ProviderId.New();

    /// <summary>
    /// Simulates the repository hydration path:
    /// CreateUser → PromoteUserToRole → InitializeRoleList → ClearDomainEvents.
    /// Domain events from construction are cleared so per-test event assertions are isolated.
    /// </summary>
    private static User BuildHydratedUser(string roleName = "User")
    {
        var user = User.CreateUser(TestEmail, TestUserName, TestIdpId);
        var roleId = ProjectRoleId.Create(DefaultRoleMap.Single(kv => kv.Value == roleName).Key);
        user.PromoteUserToRole(roleId);
        user.InitializeRoleList(DefaultRoleMap, DefaultWellKnownRoles);
        user.ClearDomainEvents();
        return user;
    }

    [Fact]
    public void Should_SetProperties_When_CreateUser()
    {
        var user = User.CreateUser(TestEmail, TestUserName, TestIdpId);

        Assert.Equal(TestEmail, user.Email);
        Assert.Equal(TestUserName, user.UserName);
        Assert.Equal(TestIdpId, user.IdentityProviderId);
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public void Should_RaiseUserCreatedEvent_When_CreateUser()
    {
        var user = User.CreateUser(TestEmail, TestUserName, TestIdpId);

        var evt = Assert.Single(user.DomainEvents.OfType<UserCreatedEvent>());
        Assert.Equal(TestUserName, evt.UserName);
        Assert.Equal(TestEmail, evt.Email);
        Assert.Equal(TestIdpId, evt.IdentityProviderId);
    }

    [Fact]
    public void Should_HaveNoAssignedProvider_When_CreateUser()
    {
        var user = User.CreateUser(TestEmail, TestUserName, TestIdpId);

        Assert.False(user.HasAssignedProvider);
        Assert.Empty(user.AssignedProviderIds);
    }

    [Fact]
    public void Should_CreateUserShell_When_FromUserName()
    {
        var shell = User.FromUserName(TestUserName);

        Assert.Equal(TestUserName, shell.UserName);
    }

    [Fact]
    public void Should_ThrowInvalidOperationException_When_SystemRoleNameAccessedBeforeInitialization()
    {
        // CreateUser does NOT call InitializeRoleList — simulates a freshly-created,
        // not-yet-persisted aggregate.
        var user = User.CreateUser(TestEmail, TestUserName, TestIdpId);
        user.PromoteUserToRole(ProjectRoleId.Create(1));

        Assert.Throws<InvalidOperationException>(() => _ = user.SystemRoleName);
    }

    [Fact]
    public void Should_ReturnCorrectRoleName_When_SystemRoleName_ForEachWellKnownRole()
    {
        Assert.Equal("User", BuildHydratedUser("User").SystemRoleName);
        Assert.Equal("Admin", BuildHydratedUser("Admin").SystemRoleName);
        Assert.Equal("ProviderAdmin", BuildHydratedUser("ProviderAdmin").SystemRoleName);
    }

    [Fact]
    public void Should_Fail_When_PromoteToProviderAdmin_UserIsAdmin()
    {
        var admin = BuildHydratedUser("Admin");

        var result = admin.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Fail_When_PromoteToProviderAdmin_WithEmptyProviderSet()
    {
        var user = BuildHydratedUser("User");

        var result = user.PromoteToProviderAdmin(new HashSet<ProviderId>());

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Succeed_When_PromoteToProviderAdmin_AsUser()
    {
        var user = BuildHydratedUser("User");

        var result = user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });

        Assert.True(result.IsSuccess);
        Assert.Equal("ProviderAdmin", user.SystemRoleName);
    }

    [Fact]
    public void Should_AssignProvider_When_PromoteToProviderAdmin()
    {
        var user = BuildHydratedUser("User");

        user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });

        Assert.True(user.HasAssignedProvider);
        Assert.Equal(ProviderId1, user.AssignedProviderIds.Single());
    }

    [Fact]
    public void Should_RaiseUserPromotedToProviderAdminEvent_When_PromoteToProviderAdmin()
    {
        var user = BuildHydratedUser("User");

        user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });

        var evt = Assert.Single(user.DomainEvents.OfType<UserPromotedToProviderAdminEvent>());
        Assert.Equal(TestUserName, evt.UserName);
        Assert.Contains(ProviderId1, evt.AssignedProviderIds);
    }

    [Fact]
    public void Should_FailWithRoleNotResolvedError_When_PromoteToProviderAdmin_RoleMapMissingProviderAdmin()
    {
        // Simulates a Gateway RbacSeed race: the role map was hydrated before ProviderAdmin existed.
        var incompleteRoleMap = new Dictionary<int, string> { { 1, "User" }, { 2, "Admin" } };
        var user = User.CreateUser(TestEmail, TestUserName, TestIdpId);
        user.PromoteUserToRole(ProjectRoleId.Create(1));
        user.InitializeRoleList(incompleteRoleMap, DefaultWellKnownRoles);
        user.ClearDomainEvents();

        var result = user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });

        Assert.False(result.IsSuccess);
        Assert.Equal(UserErrorCodes.RoleNotResolved, result.Error.Code);
        Assert.False(user.HasAssignedProvider);
    }

    [Fact]
    public void Should_Succeed_When_PromoteToProviderAdmin_AsExistingProviderAdmin()
    {
        // Re-promotion replaces the role + provider atomically.
        var user = BuildHydratedUser("ProviderAdmin");

        var result = user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId2 });

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderId2, user.AssignedProviderIds.Single());
    }

    [Fact]
    public void Should_Fail_When_ResetToPlatformUser_UserIsAdmin()
    {
        var admin = BuildHydratedUser("Admin");

        var result = admin.ResetToPlatformUser();

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Fail_When_ResetToPlatformUser_ProviderStillAssigned()
    {
        var user = BuildHydratedUser("User");
        user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });
        user.ClearDomainEvents();

        var result = user.ResetToPlatformUser();

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Succeed_When_ResetToPlatformUser_ProviderAdminAfterUnassign()
    {
        var user = BuildHydratedUser("User");
        user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });
        user.UnassignProvider(ProviderId1);
        user.ClearDomainEvents();

        var result = user.ResetToPlatformUser();

        Assert.True(result.IsSuccess);
        Assert.Equal("User", user.SystemRoleName);
    }

    [Fact]
    public void Should_ChangeTheSystemPermissionInPlace_When_PromotedAndReset()
    {
        var user = BuildHydratedUser("User");
        var systemPermission = user.SystemPermission;

        user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });

        Assert.Same(systemPermission, user.SystemPermission);
        Assert.Equal(ProjectRoleId.Create(DefaultRoleMap.Single(kv => kv.Value == "ProviderAdmin").Key), systemPermission.SystemRoleId);

        user.UnassignProvider(ProviderId1);
        user.ResetToPlatformUser();

        Assert.Same(systemPermission, user.SystemPermission);
        Assert.Equal(ProjectRoleId.Create(DefaultRoleMap.Single(kv => kv.Value == "User").Key), systemPermission.SystemRoleId);
    }

    [Fact]
    public void Should_RaiseUserDemotedToUserEvent_When_ResetToPlatformUser()
    {
        var user = BuildHydratedUser("User");
        user.PromoteToProviderAdmin(new HashSet<ProviderId> { ProviderId1 });
        user.UnassignProvider(ProviderId1);
        user.ClearDomainEvents();

        user.ResetToPlatformUser();

        var evt = Assert.Single(user.DomainEvents.OfType<UserDemotedToUserEvent>());
        Assert.Equal(TestUserName, evt.UserName);
    }

    [Fact]
    public void Should_Succeed_When_AssignProvider_ToUnassignedUser()
    {
        var user = BuildHydratedUser();

        var result = user.AssignProvider(ProviderId1);

        Assert.True(result.IsSuccess);
        Assert.True(user.HasAssignedProvider);
        Assert.Equal(ProviderId1, user.AssignedProviderIds.Single());
    }

    [Fact]
    public void Should_Fail_When_AssignProvider_AlreadyAssignedToSameProvider()
    {
        var user = BuildHydratedUser();
        user.AssignProvider(ProviderId1);

        var result = user.AssignProvider(ProviderId1);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_RaiseUserAssignedToProviderEvent_When_AssignProvider()
    {
        var user = BuildHydratedUser();

        user.AssignProvider(ProviderId1);

        var evt = Assert.Single(user.DomainEvents.OfType<UserAssignedToProviderEvent>());
        Assert.Equal(TestUserName, evt.UserName);
        Assert.Equal(ProviderId1, evt.ProviderId);
    }

    [Fact]
    public void Should_Succeed_When_UnassignProvider_WhenAssigned()
    {
        var user = BuildHydratedUser();
        user.AssignProvider(ProviderId1);
        user.ClearDomainEvents();

        var result = user.UnassignProvider(ProviderId1);

        Assert.True(result.IsSuccess);
        Assert.False(user.HasAssignedProvider);
        Assert.Empty(user.AssignedProviderIds);
    }

    [Fact]
    public void Should_Fail_When_UnassignProvider_NotAssigned()
    {
        var user = BuildHydratedUser();

        var result = user.UnassignProvider(ProviderId1);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_Fail_When_UnassignProvider_DifferentProvider()
    {
        var user = BuildHydratedUser();
        user.AssignProvider(ProviderId1);
        user.ClearDomainEvents();

        var result = user.UnassignProvider(ProviderId2);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Should_RaiseUserUnassignedFromProviderEvent_When_UnassignProvider()
    {
        var user = BuildHydratedUser();
        user.AssignProvider(ProviderId1);
        user.ClearDomainEvents();

        user.UnassignProvider(ProviderId1);

        var evt = Assert.Single(user.DomainEvents.OfType<UserUnassignedFromProviderEvent>());
        Assert.Equal(TestUserName, evt.UserName);
        Assert.Equal(ProviderId1, evt.ProviderId);
    }

    [Fact]
    public void Should_UpdateUserName_When_UpdateUserName()
    {
        var user = BuildHydratedUser();
        var newName = UserName.Create("newusername");

        var result = user.UpdateUserName(newName);

        Assert.True(result.IsSuccess);
        Assert.Equal(newName, user.UserName);
    }

    [Fact]
    public void Should_RaiseUserNameUpdatedEvent_With_OldAndNewName_When_UpdateUserName()
    {
        var user = BuildHydratedUser();
        var oldName = user.UserName;
        var newName = UserName.Create("newusername");

        user.UpdateUserName(newName);

        var evt = Assert.Single(user.DomainEvents.OfType<UserNameUpdatedEvent>());
        Assert.Equal(oldName, evt.OldUserName);
        Assert.Equal(newName, evt.NewUserName);
    }

    [Fact]
    public void Should_UpdateEmail_When_UpdateEmail()
    {
        var user = BuildHydratedUser();
        var newEmail = Email.Create("new@example.com");

        var result = user.UpdateEmail(newEmail);

        Assert.True(result.IsSuccess);
        Assert.Equal(newEmail, user.Email);
    }

    [Fact]
    public void Should_RaiseUserEmailUpdatedEvent_When_UpdateEmail()
    {
        var user = BuildHydratedUser();
        var newEmail = Email.Create("new@example.com");

        user.UpdateEmail(newEmail);

        var evt = Assert.Single(user.DomainEvents.OfType<UserEmailUpdatedEvent>());
        Assert.Equal(TestIdpId, evt.IdentityProviderId);
    }

    [Fact]
    public void Should_RaiseNoEvent_When_UpdateEmail_WithSameEmail()
    {
        var user = BuildHydratedUser();

        var result = user.UpdateEmail(TestEmail);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestEmail, user.Email);
        Assert.Empty(user.DomainEvents.OfType<UserEmailUpdatedEvent>());
    }

    [Fact]
    public void Should_SetIsDeletedTrue_When_Deactivate()
    {
        var user = BuildHydratedUser();

        var result = user.Deactivate();

        Assert.True(result.IsSuccess);
        Assert.True(user.IsDeleted);
    }

    [Fact]
    public void Should_RaiseUserDeletedEvent_When_Deactivate()
    {
        var user = BuildHydratedUser();

        user.Deactivate();

        var evt = Assert.Single(user.DomainEvents.OfType<UserDeletedEvent>());
        Assert.Equal(TestUserName, evt.UserName);
    }

    [Fact]
    public void Should_SetIsDeletedFalse_When_Reactivate()
    {
        var user = BuildHydratedUser();
        user.Deactivate();
        user.ClearDomainEvents();

        var result = user.Reactivate();

        Assert.True(result.IsSuccess);
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public void Should_RaiseUserReactivatedEvent_When_Reactivate()
    {
        var user = BuildHydratedUser();
        user.Deactivate();
        user.ClearDomainEvents();

        user.Reactivate();

        var evt = Assert.Single(user.DomainEvents.OfType<UserReactivatedEvent>());
        Assert.Equal(TestUserName, evt.UserName);
    }

    [Fact]
    public void Should_ReturnSuccess_When_UpdateUserPreferences()
    {
        var user = BuildHydratedUser();

        var result = user.UpdateUserPreferences("""{"theme":"dark"}""", UserId.New());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Should_CreatePreferenceOnFirstWrite_When_UpdateUserPreferences()
    {
        var user = BuildHydratedUser();

        user.UpdateUserPreferences("""{"theme":"dark"}""", UserId.New());

        Assert.Equal("dark", user.UserPreferences.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public void Should_RaiseUserPreferencesUpdatedEvent_When_UpdateUserPreferences()
    {
        var user = BuildHydratedUser();

        user.UpdateUserPreferences("""{"theme":"dark"}""", UserId.New());

        var evt = Assert.Single(user.DomainEvents.OfType<UserPreferencesUpdatedEvent>());
        Assert.Equal(TestUserName, evt.UserName);
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_UpdateUserPreferences_WithNull()
    {
        var user = BuildHydratedUser();

        Assert.Throws<ArgumentNullException>(() => user.UpdateUserPreferences(null!, UserId.New()));
    }

    [Fact]
    public void Should_ReturnFalse_When_HasAssignedProvider_OnFreshUser()
    {
        var user = BuildHydratedUser();

        Assert.False(user.HasAssignedProvider);
        Assert.Empty(user.AssignedProviderIds);
    }

    [Fact]
    public void Should_ReturnTrue_When_HasAssignedProvider_AfterAssignment()
    {
        var user = BuildHydratedUser();
        user.AssignProvider(ProviderId1);

        Assert.True(user.HasAssignedProvider);
        Assert.Single(user.AssignedProviderIds);
    }

    [Fact]
    public void Should_ReturnFalse_When_HasAssignedProvider_AfterUnassignment()
    {
        var user = BuildHydratedUser();
        user.AssignProvider(ProviderId1);

        user.UnassignProvider(ProviderId1);

        Assert.False(user.HasAssignedProvider);
        Assert.Empty(user.AssignedProviderIds);
    }

    [Fact]
    public void Should_ReturnEmptyJsonObject_When_UserPreferences_WithNoPreferenceSet()
    {
        var user = BuildHydratedUser();

        using var doc = user.UserPreferences;

        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Empty(doc.RootElement.EnumerateObject());
    }
}
