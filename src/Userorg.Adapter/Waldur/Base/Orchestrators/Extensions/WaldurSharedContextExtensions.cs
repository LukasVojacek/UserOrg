using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Sync;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

namespace Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

internal static class WaldurSharedContextExtensions
{
    extension(IUserOrgDbContext context)
    {
        /// <summary>
        /// Idempotent per-instance service-account bootstrap (ADR-0057/A5, D18): looked up by
        /// <see cref="WaldurServiceAccountIdentity.ServiceName"/>, created only when absent. This is
        /// the CreatedBy/AssignedBy identity every consumer handler resolves via
        /// <c>RequireServiceAccountAsync</c>, so it must run before any event for that instance is handled.
        /// A save failure is unexpected (DB outage/constraint bug), so it is logged and returned as a
        /// <see cref="Result{User, Error}"/> failure rather than rethrown.
        /// </summary>
        internal async Task<Result<User, Error>> InitializeSystemExportUser(
            ILogger logger,
            WaldurServiceAccountIdentity identity,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(identity);

            var dbUser = await context.UsersQuery
                .FirstOrDefaultAsync(u => u.IdentityProviderUserId == identity.ServiceName, ct);
            if (dbUser is not null)
            {
                return Result<User, Error>.Success(dbUser);
            }

            User waldurUser = new()
            {
                Id = Guid.CreateVersion7(),
                IdentityProviderUserId = identity.ServiceName,
                Email = identity.ServiceAccountEmail,
                UserName = identity.ServiceAccountUserName,
                AssignedToProjects = [],
                CreatedAt = DateTime.UtcNow,
            };

            try
            {
                logger.CreatingWaldurServiceAccount(identity.ServiceName, waldurUser.Email);
                context.Add(waldurUser);
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                logger.FailedToSaveWaldurServiceAccount(ex, identity.ServiceName);
                return Result<User, Error>.Failure(Errors.FailedToSaveWaldurServiceAccount(identity.ServiceName));
            }

            return Result<User, Error>.Success(waldurUser);
        }
    }
}