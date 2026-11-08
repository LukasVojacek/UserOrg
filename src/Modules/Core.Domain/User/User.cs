using System.Text.Json;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.User;

/// <summary>
/// User Aggregate Root (Natural IDs: UserName, IdentityProviderId)
/// Id, CreatedAt, ModifiedAt managed as EF shadow properties.
/// _systemPermissions: backing field for EF – query filter ensures only system roles (ProjectId IS NULL) are loaded.
/// _userPreference: backing field for EF – UserPreferences is a computed projection.
/// _assignedProviderIds: backing field for EF – 1:N mapping; DB schema migration from single column to junction table is pending.
/// </summary>
public sealed class User : AggregateRoot, ISoftDeletableEntity
{
    private IReadOnlyDictionary<int, string> _roleNameMap = new Dictionary<int, string>();
    private WellKnownRoles _wellKnownRoles = new();
    private bool _roleMapInitialized = false;
    private IList<UserPermission> _systemPermissions = [];
    private UserPreference? _userPreference = null;

    //TODO: Make ProviderName unique and use that as reference instead of ProviderId to avoid cross-aggregate coupling. Requires schema change.
    // EF backing field – mapped to AssignedProviderId column. Exposed as list in preparation for future 1:N junction table migration.
    private ProviderId? _assignedProviderId;

    public bool HasAssignedProvider => _assignedProviderId.HasValue;
    public IReadOnlyList<ProviderId> AssignedProviderIds => _assignedProviderId.HasValue ? [_assignedProviderId.Value] : [];
    internal int SystemPermissionsCount => _systemPermissions.Count;
    internal UserPermission SystemPermission => _systemPermissions.Single();

    public Email Email { get; private set; }
    public UserName UserName { get; private set; }
    public IdpId IdentityProviderId { get; private set; }
    public bool IsDeleted { get; private set; }

    public JsonDocument UserPreferences => _userPreference is null
        ? JsonDocument.Parse("{}")
        : JsonDocument.Parse(_userPreference.Data);

    public string SystemRoleName
    {
        get
        {
            ThrowIfRoleMapNotInitialized();
            return _roleNameMap[_systemPermissions.Single().SystemRoleId.Value];
        }
    }

    // EF materialization + factory only; closes the implicit public parameterless constructor so
    // `new User()` cannot bypass CreateUser.
    private User() { }

    /// <summary>
    /// Creates a new user with the default system role (User).
    /// First user in system should be promoted to Admin via PromoteUserToRole() in the command handler.
    /// </summary>
    public static User CreateUser(Email email, UserName userName, IdpId idpId)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(userName);

        var user = new User
        {
            Email = email,
            UserName = userName,
            IdentityProviderId = idpId,
            IsDeleted = false,
        };

        user.AddDomainEvent(new UserCreatedEvent(userName, email, idpId));
        return user;
    }

    /// <summary>
    /// Reconstructs a User shell from UserName only – for cross-aggregate reference resolution.
    /// </summary>
    public static User FromUserName(UserName userName) => new() { UserName = userName };

    /// <summary>
    /// Assigns the given system role – called by the repository on AddAsync and by the command handler when promoting to Admin.
    /// </summary>
    internal void PromoteUserToRole(ProjectRoleId roleId)
    {
        _systemPermissions.Clear();
        _systemPermissions.Add(UserPermission.CreateSystemPermission(roleId));
    }

    /// <summary>
    /// Promotes user to ProviderAdmin and assigns them to the given providers atomically.
    /// Fails if the user is currently Admin or if no providers are supplied.
    /// </summary>
    public Result<Error> PromoteToProviderAdmin(IReadOnlySet<ProviderId> providerIds)
    {
        if (SystemRoleName == _wellKnownRoles.DefaultAdminRoleName)
        {
            return Result<Error>.Failure(UserErrors.AdminRoleCannotBeChanged());
        }

        if (providerIds.Count == 0)
        {
            return Result<Error>.Failure(UserErrors.ProviderAdminRequiresAtLeastOneProvider());
        }

        if (!TryFindRoleId(_wellKnownRoles.DefaultProviderAdminRoleName, out var providerAdminRoleId))
        {
            return Result<Error>.Failure(UserErrors.RoleNotResolved(_wellKnownRoles.DefaultProviderAdminRoleName));
        }

        _systemPermissions.Single().ChangeRole(providerAdminRoleId);
        _assignedProviderId = providerIds.Single();

        AddDomainEvent(new UserPromotedToProviderAdminEvent(UserName, AssignedProviderIds, IdentityProviderId));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Demotes the user from ProviderAdmin back to the platform User role.
    /// Fails if the user is currently Admin or still assigned to any provider.
    /// </summary>
    public Result<Error> ResetToPlatformUser()
    {
        if (SystemRoleName == _wellKnownRoles.DefaultAdminRoleName)
        {
            return Result<Error>.Failure(UserErrors.AdminRoleCannotBeChanged());
        }

        if (_assignedProviderId.HasValue)
        {
            return Result<Error>.Failure(UserErrors.CannotDemoteWithActiveProviderAssignments());
        }

        _systemPermissions.Single().ChangeRole(FindRoleId(_wellKnownRoles.DefaultUserRoleName));

        AddDomainEvent(new UserDemotedToUserEvent(UserName, IdentityProviderId));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Assigns the user to an additional provider.
    /// Fails if already assigned to the given provider.
    /// </summary>
    public Result<Error> AssignProvider(ProviderId providerId)
    {
        if (_assignedProviderId == providerId)
        {
            return Result<Error>.Failure(UserErrors.AlreadyAssignedToProvider(providerId));
        }

        _assignedProviderId = providerId;
        AddDomainEvent(new UserAssignedToProviderEvent(UserName, providerId, IdentityProviderId));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Removes the user's assignment from a provider.
    /// Fails if the user is not assigned to the given provider.
    /// </summary>
    public Result<Error> UnassignProvider(ProviderId providerId)
    {
        if (_assignedProviderId != providerId)
        {
            return Result<Error>.Failure(UserErrors.NotAssignedToProvider(providerId));
        }

        _assignedProviderId = null;
        AddDomainEvent(new UserUnassignedFromProviderEvent(UserName, providerId, IdentityProviderId));
        return Result<Error>.Success();
    }

    public Result<Error> UpdateUserName(UserName userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        var oldUserName = UserName;
        UserName = userName;
        AddDomainEvent(new UserNameUpdatedEvent(oldUserName, userName, IdentityProviderId));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Updates the user's e-mail address and raises a domain event.
    /// No-op if the new email equals the current email (prevents phantom events during JIT sync).
    ///
    /// Called during v2 user initialization to sync IdP e-mail claim with the stored User entity,
    /// mirroring v1's UserInitializationEndpointFilter (§52-58). The no-op guard ensures
    /// per-login sync does not emit an event on unchanged email, avoiding unnecessary cache evictions.
    /// </summary>
    public Result<Error> UpdateEmail(Email email)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (email == Email)
        {
            return Result<Error>.Success();
        }

        var oldEmail = Email;
        Email = email;
        AddDomainEvent(new UserEmailUpdatedEvent(oldEmail, email, IdentityProviderId));
        return Result<Error>.Success();
    }

    public Result<Error> Deactivate()
    {
        IsDeleted = true;
        AddDomainEvent(new UserDeletedEvent(UserName, IdentityProviderId));
        return Result<Error>.Success();
    }

    public Result<Error> Reactivate()
    {
        IsDeleted = false;
        AddDomainEvent(new UserReactivatedEvent(UserName, IdentityProviderId));
        return Result<Error>.Success();
    }

    public Result<Error> UpdateUserPreferences(string preferencesJson, UserId userId)
    {
        ArgumentNullException.ThrowIfNull(preferencesJson);

        if (_userPreference is null)
        {
            // First write: create the preference child (the FK is the user's own id).
            _userPreference = UserPreference.Create(userId, JsonDocument.Parse(preferencesJson));
        }
        else
        {
            _userPreference.UpdateData(preferencesJson);
        }

        AddDomainEvent(new UserPreferencesUpdatedEvent(UserName, IdentityProviderId));
        return Result<Error>.Success();
    }

    internal void InitializeRoleList(IReadOnlyDictionary<int, string> roleMap, WellKnownRoles wellKnownRoles)
    {
        ArgumentNullException.ThrowIfNull(roleMap);
        ArgumentNullException.ThrowIfNull(wellKnownRoles);
        _roleNameMap = roleMap;
        _wellKnownRoles = wellKnownRoles;
        _roleMapInitialized = true;
    }

    private void ThrowIfRoleMapNotInitialized()
    {
        if (!_roleMapInitialized)
        {
            throw new InvalidOperationException(
                $"""
                Role map has not been initialized on User '{UserName.Value}'.
                This happens when role-dependent operations are called on a newly created User before it is persisted and reloaded. 
                {nameof(InitializeRoleList)} is called automatically by the repository during FindAsync hydration.
                """);
        }
    }

    private ProjectRoleId FindRoleId(string roleName)
    {
        ThrowIfRoleMapNotInitialized();
        return ProjectRoleId.Create(_roleNameMap.Single(kv => kv.Value.Equals(roleName, StringComparison.OrdinalIgnoreCase)).Key);
    }

    /// <summary>
    /// Guarded counterpart to <see cref="FindRoleId"/> for callers that must report a missing role as a
    /// domain failure instead of throwing (e.g. a Gateway RbacSeed race leaving the role map incomplete).
    /// </summary>
    private bool TryFindRoleId(string roleName, out ProjectRoleId roleId)
    {
        ThrowIfRoleMapNotInitialized();
        foreach (var (id, name) in _roleNameMap)
        {
            if (name.Equals(roleName, StringComparison.OrdinalIgnoreCase))
            {
                roleId = ProjectRoleId.Create(id);
                return true;
            }
        }

        roleId = default;
        return false;
    }
}