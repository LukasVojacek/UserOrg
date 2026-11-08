using Core.DataAccess.Events;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using UserOrg.Shared;

using WellKnownRoles = Core.Domain.User.WellKnownRoles;

namespace Core.DataAccess.Contexts;

/// <summary>
/// Design-time factory used by the EF Core tooling (e.g. <c>dotnet ef migrations add</c>).
/// Not used at runtime — the application composes <see cref="DatabaseContext"/> through
/// <c>AddCoreDataAccess</c>. EF prefers this factory over building the startup host, so the
/// migration workflow no longer depends on the API host (Vault, secrets, etc.) booting.
///
/// The connection string is a placeholder — <c>migrations add</c> only diffs the model against
/// the snapshot and never opens a connection. The role-service stub is never invoked during
/// model building.
/// </summary>
internal sealed class DatabaseContextDesignTimeFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    public DatabaseContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseSqlServer(
                "Server=localhost;Database=DesignTime;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsAssembly(typeof(DatabaseContext).Assembly))
            .Options;

        return new DatabaseContext(
            options,
            new DomainEventChannel(),
            new DesignTimeRoleService(),
            Options.Create(new WellKnownRoles()),
            NullLogger<DatabaseContext>.Instance);
    }

    /// <summary>No-op <see cref="IRoleService"/> — only needed so the context can be constructed.</summary>
    private sealed class DesignTimeRoleService : IRoleService
    {
        public Task<Result<IList<RoleInfo>, Error>> ListRoles(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<RoleInfo, Error>> CreateRole(string roleName, string description, string category, int level, HashSet<PermissionKey> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<RoleInfo, Error>> GetRole(string roleName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<IReadOnlyDictionary<int, string>, Error>> ListRoleNames(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<Error>> UpdateRolePermissions(string roleName, HashSet<PermissionKey> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<bool, Error>> UpdateRoleState(string roleName, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<Error>> UpdateRoleLevel(string roleName, int level, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<Error>> UpdateRoleCategory(string roleName, string category, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<Error>> UpdateRoleDescription(string roleName, string? description, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task<Result<Error>> DeleteRole(string roleName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");

        public Task InvalidateRoleListsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Design-time only.");
    }
}