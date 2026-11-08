using Microsoft.EntityFrameworkCore;

namespace Forge.Core.Features.UserOrgRoles;

internal class UserOrgDbContext : DbContext
{
    public UserOrgDbContext(DbContextOptions<UserOrgDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("dbo");

        // Configure keyless DTO for raw SQL mapping
        modelBuilder.Entity<UserPermissionDto>(eb =>
        {
            eb.HasNoKey();
            eb.ToView(null); // not mapped to a real database view
        });

        modelBuilder.Entity<ProviderNameRow>(eb =>
        {
            eb.HasNoKey();
            eb.ToView(null); // not mapped to a real database view
        });
    }

    public Task<List<UserPermissionDto>> GetUserPermissionsAsync(string identityProviderUserId, CancellationToken cancellationToken = default)
    {

        return Set<UserPermissionDto>()
            .FromSqlInterpolated($@"select u.IdentityProviderUserId as IdentityProviderUserId,
                                                                       up.ProjectId as ProjectId,
                                                                       p.ShortName as ShortName,
                                                                       up.SystemRoleId as SystemRoleId,
                                                                       '' as SystemRoleName
                                                            from UserPermission up
                                                                 inner join [User] u on u.Id = up.UserId
                                                                 left join dbo.Project p on up.ProjectId = p.Id
                                                            where u.IdentityProviderUserId= {identityProviderUserId}
                                                              and u.IsDeleted = 0
                                                              and up.IsDeleted = 0
                                                              and (up.ProjectId is null or p.IsDeleted = 0)")
            //TODO: SystemRole on UserPermission table will be just Id, so no need to join SystemRole table in future
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves a provider's natural name from its surrogate Guid id. Deliberately omits the IsDeleted
    /// filter so a deactivated provider can still be identified (e.g. for reactivation). Returns null
    /// when no provider matches.
    /// </summary>
    public async Task<string?> GetProviderNameByIdAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var rows = await Set<ProviderNameRow>()
            .FromSqlInterpolated($"select Name as Name from Provider where Id = {providerId}")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Count > 0 ? rows[0].Name : null;
    }

    /// <summary>
    /// Resolves the natural name of the provider assigned to an interactive user, keyed by their
    /// identity-provider subject. Seeks the unique <c>UX_User_IdentityProviderUserId</c> index, which already
    /// INCLUDEs <c>AssignedProviderId</c>. Returns null when the user is unknown, soft-deleted, or has no
    /// provider assigned.
    /// </summary>
    public async Task<string?> GetAssignedProviderNameByIdpUserIdAsync(string idpUserId, CancellationToken cancellationToken = default)
    {
        var rows = await Set<ProviderNameRow>()
            .FromSqlInterpolated($@"select p.Name as Name
                                    from [User] u
                                         inner join Provider p on p.Id = u.AssignedProviderId
                                    where u.IdentityProviderUserId = {idpUserId}
                                      and u.IsDeleted = 0")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Count > 0 ? rows[0].Name : null;
    }
}