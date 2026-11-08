using Core.DataAccess.Exceptions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Contexts;

internal sealed partial class DatabaseContext : IRepository<User, UserName>
{
    private IQueryable<User> _userIncludes => Users
        .Include("_userPreference")
        .Include("_systemPermissions")
        .AsSplitQuery();

    public void Delete(User aggregate, CancellationToken cancellationToken) => Users.Remove(aggregate);

    ValueTask<User?> IRepository<User, UserName>.AddAsync(User aggregate, CancellationToken cancellationToken) =>
        AddUserAsync(aggregate, cancellationToken);

    async ValueTask<User?> IRepository<User, UserName>.FindAsync(UserName userName, CancellationToken cancellationToken)
    {
        var user = await _userIncludes.FirstOrDefaultAsync(p => p.UserName == userName, cancellationToken);
        if (user is null)
            return null;

        var rolesRequest = await _roleService.ListRoleNames(cancellationToken);
        if (!rolesRequest.IsSuccess)
            throw new RoleServiceException();

        return HydrateUser(user, rolesRequest.Value);
    }
}

internal sealed partial class DatabaseContext : IRepository<User, IdpId>
{
    ValueTask<User?> IRepository<User, IdpId>.AddAsync(User aggregate, CancellationToken cancellationToken) =>
        AddUserAsync(aggregate, cancellationToken);

    async ValueTask<User?> IRepository<User, IdpId>.FindAsync(IdpId idpId, CancellationToken cancellationToken)
    {
        var user = await _userIncludes.FirstOrDefaultAsync(p => p.IdentityProviderId == idpId, cancellationToken);
        if (user is null)
            return null;

        var rolesRequest = await _roleService.ListRoleNames(cancellationToken);
        if (!rolesRequest.IsSuccess)
            throw new RoleServiceException();

        return HydrateUser(user, rolesRequest.Value);
    }
}

internal sealed partial class DatabaseContext
{
    /// <summary>
    /// Shared by both <c>AddAsync</c> overloads (<c>UserName</c>- and <c>IdpId</c>-keyed) — they differ
    /// only in the lookup key, not in how a user is created.
    /// First-user bootstrap (ADR-0068 §3, parity with v1 <c>UserService.CreateUser</c>): the first user
    /// added to an empty system becomes the default admin; every later user becomes the default user.
    /// Role assignment lives here (not a use-case) because <c>User.PromoteUserToRole</c> is internal to
    /// <c>Core.Domain</c> with <c>InternalsVisibleTo("Core.DataAccess")</c> only.
    /// Throws <see cref="RoleServiceException"/> if the configured default role is absent from the role
    /// service's role list — the role service did not serve a role we are configured to require, the same
    /// class of upstream-contract failure as the service call itself failing.
    /// </summary>
    private async ValueTask<User?> AddUserAsync(User aggregate, CancellationToken cancellationToken)
    {
        var rolesRequest = await _roleService.ListRoleNames(cancellationToken);
        if (!rolesRequest.IsSuccess)
            throw new RoleServiceException();

        var usersExist = await Users.AnyAsync(cancellationToken);
        var defaultRoleName = usersExist
            ? _wellKnownRoleOptions.Value.DefaultUserRoleName
            : _wellKnownRoleOptions.Value.DefaultAdminRoleName;

        var userRole = rolesRequest.Value.FirstOrDefault(u => u.Value.Equals(defaultRoleName, StringComparison.OrdinalIgnoreCase));
        if (userRole.Value is null)
            throw new RoleServiceException();

        aggregate.PromoteUserToRole(ProjectRoleId.Create(userRole.Key));

        await Users.AddAsync(aggregate, cancellationToken);
        Entry(aggregate).Property("Id").CurrentValue = UserId.New();
        RegisterAddedAggregateRoot(aggregate);

        return aggregate;
    }

    /// <summary>
    /// Finalizes aggregate hydration and validates system role invariants.
    /// Throws <see cref="InvalidUserRoleStateException"/> when the user has zero, multiple,
    /// or unrecognized system roles — indicating DB corruption or a misconfigured role service.
    /// </summary>
    private User HydrateUser(User user, IReadOnlyDictionary<int, string> roleMap)
    {
        user.InitializeRoleList(roleMap, _wellKnownRoleOptions.Value);

        ValidateSystemRole(user);
        return user;
    }

    private void ValidateSystemRole(User user)
    {
        if (user.SystemPermissionsCount != 1)
        {
            _logger.LogError(
                "User '{UserName}' has {Count} system permission(s) loaded; expected exactly 1. Possible DB corruption or query filter misconfiguration.",
                user.UserName.Value, user.SystemPermissionsCount);

            throw new InvalidUserRoleStateException(
                $"User '{user.UserName.Value}' has {user.SystemPermissionsCount} system permission(s); expected exactly 1.");
        }

        var opts = _wellKnownRoleOptions.Value;
        var isKnownRole = user.SystemRoleName.Equals(opts.DefaultUserRoleName, StringComparison.OrdinalIgnoreCase)
            || user.SystemRoleName.Equals(opts.DefaultAdminRoleName, StringComparison.OrdinalIgnoreCase)
            || user.SystemRoleName.Equals(opts.DefaultProviderAdminRoleName, StringComparison.OrdinalIgnoreCase);

        if (!isKnownRole)
        {
            _logger.LogError(
                "User '{UserName}' has unrecognized system role '{RoleName}'. Configured roles: {UserRole}, {AdminRole}, {ProviderAdminRole}.",
                user.UserName.Value, user.SystemRoleName,
                opts.DefaultUserRoleName, opts.DefaultAdminRoleName, opts.DefaultProviderAdminRoleName);

            throw new InvalidUserRoleStateException(
                $"User '{user.UserName.Value}' has unrecognized system role '{user.SystemRoleName}'.");
        }
    }
}