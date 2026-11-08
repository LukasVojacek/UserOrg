using Forge.Core.Persistence;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Forge.Core.Features.Role.Services;

internal sealed class RoleService(
    IDbContextFactory<ForgeDbContext> dbContextFactory,
    HybridCache cache,
    ILogger<RoleService> logger) : IRoleService
{
    private const string ListRolesCacheKey = "RoleService:ListRoles";
    private const string ListRoleNamesCacheKey = "RoleService:ListRoleNames";

    private static readonly HybridCacheEntryOptions _rolesCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    public async Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<RoleInfo, Error>.Failure(RoleErrors.InvalidRoleName);
        }

        using var context = dbContextFactory.CreateDbContext();

        var exists = await context.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Name == roleName, cancellationToken);

        if (exists)
        {
            return Result<RoleInfo, Error>.Failure(RoleErrors.RoleAlreadyExists(roleName));
        }

        if (level == 0)
        {
            level = 1;
        }

        Persistence.Role? role = null;

        if (permissions.Count > 0)
        {
            var resolved = await context.Permissions
                .Where(p => permissions.Contains(p.Key))
                .ToListAsync(cancellationToken);

            role = new Persistence.Role
            {
                Name = roleName,
                Description = description,
                RolePermissions = resolved,
                Level = level
            };
        }

        role ??= new Persistence.Role
        {
            Name = roleName,
            Description = description,
            Level = level,
            RolePermissions = []
        };

        context.Roles.Add(role);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.RolePersistError(roleName, ex);

            return Result<RoleInfo, Error>.Failure(RoleErrors.DatabaseUpdateError(roleName));
        }

        await InvalidateRoleListsAsync(cancellationToken);

        return Result<RoleInfo, Error>.Success(role.ToRoleInfo());
    }

    public async Task InvalidateRoleListsAsync(CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(CacheTags.RoleList, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.RoleNames, cancellationToken);
    }

    public async Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<RoleInfo, Error>.Failure(RoleErrors.InvalidRoleName);
        }

        using var context = dbContextFactory.CreateDbContext();

        var role = await context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .Where(r => r.Name == roleName)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
        {
            return Result<RoleInfo, Error>.Failure(RoleErrors.RoleNotFound(roleName));
        }

        return Result<RoleInfo, Error>.Success(role.ToRoleInfo());
    }
    public async Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken)
    {
        // Cache only the plain data — Result<T,E> is not JSON-serializable by HybridCache
        // because its Error property throws InvalidOperationException("Result is successful")
        // when the result is successful and System.Text.Json reads all public properties.
        var roleInfos = await cache.GetOrCreateAsync(
            ListRolesCacheKey,
            async ct =>
            {
                using var context = dbContextFactory.CreateDbContext();
                var roles = await context.Roles
                    .AsNoTracking()
                    .Include(r => r.RolePermissions)
                    .OrderBy(r => r.Name)
                    .ToListAsync(ct);
                return roles.Select(r => r.ToRoleInfo()).ToList();
            },
            options: _rolesCacheOptions,
            tags: [CacheTags.RoleList],
            cancellationToken: cancellationToken);

        return Result<IList<RoleInfo>, Error>.Success(roleInfos);
    }

    public async Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken)
    {
        // Cache Dictionary<int,string> directly — concrete type, fully serializable by System.Text.Json.
        var names = await cache.GetOrCreateAsync(
            ListRoleNamesCacheKey,
            async ct =>
            {
                using var context = dbContextFactory.CreateDbContext();
                return await context.Roles
                    .AsNoTracking()
                    .OrderBy(r => r.Id)
                    .ToDictionaryAsync(r => r.Id, r => r.Name ?? string.Empty, ct);
            },
            options: _rolesCacheOptions,
            tags: [CacheTags.RoleNames],
            cancellationToken: cancellationToken);

        return Result<IReadOnlyDictionary<int, string>, Error>.Success(names);
    }

    public async Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<Error>.Failure(RoleErrors.InvalidRoleName);
        }

        using var context = dbContextFactory.CreateDbContext();

        var roleEntity = await context.Roles
            .Include(r => r.RolePermissions)
            .Where(r => r.Name == roleName)
            .FirstOrDefaultAsync(cancellationToken);

        if (roleEntity is null)
        {
            return Result<Error>.Failure(RoleErrors.RoleNotFound(roleName));
        }

        if (permissions.Count == 0)
        {
            roleEntity.RolePermissions.Clear();
            await context.SaveChangesAsync(cancellationToken);
            await InvalidateRoleListsAsync(cancellationToken);
            return Result<Error>.Success();

        }
        var resolved = await context.Permissions
                .Where(p => permissions.Contains(p.Key))
                .ToListAsync(cancellationToken);

        if (resolved.Count < permissions.Count)
        {
            var missing = permissions.Select(pk => pk.Value).Except(resolved.Select(p => p.Key.Value)).ToList();
            logger.RolePermissionUpdateFailed(roleName, string.Join(", ", missing));
            return Result<Error>.Failure(RoleErrors.PermissionsNotFound(missing));
        }

        // Use diff-based approach for many-to-many updates
        // This ensures EF Core properly tracks changes in the join table
        var currentPermissions = roleEntity.RolePermissions.ToHashSet();
        var targetPermissions = resolved.ToHashSet();

        var toRemove = currentPermissions.Except(targetPermissions).ToList();
        var toAdd = targetPermissions.Except(currentPermissions).ToList();

        foreach (var permission in toRemove)
        {
            roleEntity.RolePermissions.Remove(permission);
        }

        foreach (var permission in toAdd)
        {
            roleEntity.RolePermissions.Add(permission);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.RolePersistError(roleName, ex);
            return Result<Error>.Failure(RoleErrors.DatabaseUpdateError(roleName));
        }
        await InvalidateRoleListsAsync(cancellationToken);
        logger.UpdatedRolePermissions(roleName);
        return Result<Error>.Success();
    }
    public async Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<bool, Error>.Failure(RoleErrors.InvalidRoleName);

        }

        using var context = dbContextFactory.CreateDbContext();

        var entity = await context.Roles
            .Where(r => r.Name == roleName)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return Result<bool, Error>.Failure(RoleErrors.RoleNotFound(roleName));
        }

        if (entity.IsActive == isActive)
        {
            return Result<bool, Error>.Success(isActive);
        }

        entity.IsActive = isActive;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.ErrorSavingRoleState(roleName, isActive ? "active" : "inactive", ex);
            throw;
        }
        await InvalidateRoleListsAsync(cancellationToken);
        logger.RoleStatusChanged(roleName, isActive ? "active" : "inactive");
        return Result<bool, Error>.Success(isActive);
    }

    public async Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken)
    {
        if (level <= 0)
        {
            logger.InvalidRoleLevelProvided(roleName, level);
            level = 1;
        }

        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<Error>.Failure(RoleErrors.InvalidRoleName);

        }

        using var context = dbContextFactory.CreateDbContext();

        var entity = await context.Roles
            .Where(r => r.Name == roleName)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return Result<Error>.Failure(RoleErrors.RoleNotFound(roleName));
        }

        if (entity is { IsActive: false })
        {
            return Result<Error>.Failure(RoleErrors.RoleNotActive(roleName));
        }

        entity.Level = level;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.ErrorSavingRoleLevel(roleName, level, ex);
            throw;
        }
        await InvalidateRoleListsAsync(cancellationToken);
        logger.RoleLevelChanged(roleName, level);
        return Result<Error>.Success();
    }

    public async Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<Error>.Failure(RoleErrors.InvalidRoleName);
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return Result<Error>.Failure(RoleErrors.InvalidCategoryName);
        }

        using var context = dbContextFactory.CreateDbContext();

        var entity = await context.Roles
            .Where(r => r.Name == roleName)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return Result<Error>.Failure(RoleErrors.RoleNotFound(roleName));
        }

        if (entity is { IsActive: false })
        {
            return Result<Error>.Failure(RoleErrors.RoleNotActive(roleName));
        }

        entity.Category = category;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.ErrorSavingRoleCategory(roleName, category, ex);
            throw;
        }
        await InvalidateRoleListsAsync(cancellationToken);
        logger.RoleCategoryChanged(roleName, category);
        return Result<Error>.Success();
    }

    public async Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Result<Error>.Failure(RoleErrors.InvalidRoleName);
        }

        using var context = dbContextFactory.CreateDbContext();

        var entity = await context.Roles
            .Where(r => r.Name == roleName)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return Result<Error>.Failure(RoleErrors.RoleNotFound(roleName));
        }

        if (entity is { IsActive: false })
        {
            return Result<Error>.Failure(RoleErrors.RoleNotActive(roleName));
        }

        entity.Description = description;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Error saving role description for {RoleName}", roleName);
            throw;
        }
        await InvalidateRoleListsAsync(cancellationToken);
        logger.LogInformation("Role {RoleName} description updated", roleName);
        return Result<Error>.Success();
    }

    public Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken)
    {
        // Role deletion is not allowed - use soft delete via UpdateRoleState instead
        return Task.FromResult(
            Result<Error>.Failure(
                new Error("RoleDeletionNotAllowed", Lukdrasil.Error.ErrorSeverity.Error,
                    "Role deletion is not permitted. Use deactivation instead.")));
    }
}