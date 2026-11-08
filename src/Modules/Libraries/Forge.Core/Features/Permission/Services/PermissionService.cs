using Forge.Core.Persistence;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Forge.Core.Features.Permission.Services;

internal sealed class PermissionService(
        IDbContextFactory<ForgeDbContext> dbContextFactory,
    ILogger<PermissionService> logger) : IPermissionService
{
    public async Task<Result<PermissionInfo, Error>> CreatePermission(PermissionKey key, string description, CancellationToken cancellationToken)
    {
        using var context = dbContextFactory.CreateDbContext();

        var exists = await context.Permissions
            .AsNoTracking()
            .AnyAsync(p => p.Key == key, cancellationToken);

        if (exists)
        {
            logger.SkippingExistingPermission(key.Value);
            return Result<PermissionInfo, Error>.Failure(PermissionErrors.PermissionAlreadyExists(key));
        }

        var permission = new Persistence.Permission(key, description);
        context.Permissions.Add(permission);
        await context.SaveChangesAsync(cancellationToken);

        return Result<PermissionInfo, Error>.Success(new PermissionInfo(permission.Key, permission.Description ?? string.Empty));
    }

    public async Task<Result<Error>> DeletePermission(PermissionKey key, CancellationToken cancellationToken)
    {

        using var context = dbContextFactory.CreateDbContext();

        var permission = await context.Permissions
            .Where(p => p.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (permission is null)
        {
            return Result<Error>.Failure(PermissionErrors.PermissionNotFound(key));
        }

        var assigned = await context.Roles
            .AsNoTracking()
            .AnyAsync(r => r.RolePermissions.Any(p => p.Id == permission.Id), cancellationToken);

        if (assigned)
        {
            logger.PermissionDeleteRefusedAssigned(key.Value);
            return Result<Error>.Failure(PermissionErrors.PermissionAssignedToRoles(key));
        }

        context.Permissions.Remove(permission);
        await context.SaveChangesAsync(cancellationToken);

        logger.DeletedPermission(key.Value);

        return Result<Error>.Success();
    }

    public async Task<Result<PermissionInfo, Error>> GetPermission(PermissionKey key, CancellationToken cancellationToken)
    {
        if (key == default)
        {
            return Result<PermissionInfo, Error>.Failure(PermissionErrors.InvalidPermissionKey);
        }

        using var context = dbContextFactory.CreateDbContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var projected = await context.Permissions
            .Where(p => p.Key == key)
            .Select(p => new { p.Key, p.Description })
            .FirstOrDefaultAsync(cancellationToken);

        if (projected is null)
        {
            return Result<PermissionInfo, Error>.Failure(PermissionErrors.PermissionNotFound(key));
        }

        return Result<PermissionInfo, Error>.Success(new PermissionInfo(projected.Key, projected.Description ?? string.Empty));
    }

    public async Task<Result<IList<PermissionInfo>, Error>> ListPermissions(CancellationToken cancellationToken)
    {
        using var context = dbContextFactory.CreateDbContext();

        var list = await context.Permissions
            .AsNoTracking()
            .OrderBy(p => EF.Property<string>(p, "Key"))
            .Select(p => p.ToPermissionInfo())
            .ToListAsync(cancellationToken);

        return Result<IList<PermissionInfo>, Error>.Success(list);

    }

    public async Task<Result<bool, Error>> IsPermissionAssignedToRoles(PermissionKey key, CancellationToken cancellationToken)
    {
        using var context = dbContextFactory.CreateDbContext();

        var permission = await context.Permissions
            .AsNoTracking()
            .Where(p => p.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (permission is null)
        {
            return Result<bool, Error>.Failure(PermissionErrors.PermissionNotFound(key));
        }

        var assigned = await context.Roles
            .AsNoTracking()
            .AnyAsync(r => r.RolePermissions.Any(p => p.Id == permission.Id), cancellationToken);

        return Result<bool, Error>.Success(assigned);
    }
}