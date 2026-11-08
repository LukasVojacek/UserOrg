using Core.DataAccess.Exceptions;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.Features.UserInfo.UseCases;

/// <summary>
/// Resolve-or-create for the authenticated caller (ADR-0068 §1-2, §6): find the <see cref="Domain.User.User"/>
/// row by the caller's normalized <see cref="IdpId"/>, create it just-in-time on first login, and sync the
/// stored e-mail with the token's when they differ. The system role assignment on creation is handled by the
/// repository adapter (<c>DatabaseContext.UserRepository.AddAsync</c>), not here.
/// </summary>
internal static class CallerProvisioning
{
    internal sealed class AuditCategory;

    /// <summary>
    /// Fails with <see cref="CallerProvisioningErrors.NotProvisionable"/> without writing anything when the
    /// token lacks the claims needed to provision (no <c>email</c> or no <c>preferred_username</c>) — the
    /// caller must translate that into 404 <c>caller.not_provisioned</c>, never attempt a write with
    /// incomplete identity data. Fails with <see cref="CallerProvisioningErrors.Deactivated"/> when the
    /// caller's row exists but is soft-deleted (hidden from <see cref="IRepository{TAggregate,TKey}.FindAsync"/>
    /// by the <c>!IsDeleted</c> query filter): a deactivated user must never be re-provisioned as a fresh one.
    /// </summary>
    internal static async Task<Result<Error>> ProvisionAsync(
        Caller caller,
        IRepository<Domain.User.User, IdpId> users,
        IUserInfoQuery userInfoQuery,
        IUnitOfWork unitOfWork,
        IAuditLogger<AuditCategory> auditLogger,
        ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (caller.Email is not { } email || caller.UserName is not { } userName)
            return Result<Error>.Failure(CallerProvisioningErrors.NotProvisionable());

        var user = await users.FindAsync(caller.IdpId, ct);
        if (user is null)
        {
            if (await userInfoQuery.IsDeactivatedAsync(caller.IdpId, ct))
            {
                var deactivated = CallerProvisioningErrors.Deactivated();
                await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(AuditActions.UserSelfProvisioned, caller) with
                {
                    TargetType = AuditActions.TargetTypes.User,
                    TargetId = caller.IdpId.Value,
                    Reason = deactivated.Code,
                }, logger);
                return Result<Error>.Failure(deactivated);
            }

            await users.AddAsync(Domain.User.User.CreateUser(email, userName, caller.IdpId), ct);
            if (await SaveOrConvergeOnWinnerAsync(caller.IdpId, users, unitOfWork, ct))
            {
                await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(AuditActions.UserSelfProvisioned, caller) with
                {
                    TargetType = AuditActions.TargetTypes.User,
                    TargetId = caller.IdpId.Value,
                }, logger);
            }

            return Result<Error>.Success();
        }

        user.UpdateEmail(email); // no-ops (no event) when the e-mail is unchanged
        await unitOfWork.SaveChangesAsync(ct);
        return Result<Error>.Success();
    }

    /// <summary>
    /// Saves the just-added user, or — when a concurrent first login for the same sub won the
    /// UX_User_IdentityProviderUserId race first (ADR-0068 §6) — swallows the resulting DbUpdateException
    /// and confirms the winner's row now exists. Any other DbUpdateException, or a missing winner row, is a
    /// genuine server error and is left to surface rather than looping or masking it.
    /// </summary>
    private static async Task<bool> SaveOrConvergeOnWinnerAsync(
        IdpId idpId,
        IRepository<Domain.User.User, IdpId> users,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (UserIdentityConflictClassifier.IsIdentityProviderConflict(ex))
        {
            if (await users.FindAsync(idpId, ct) is null)
                throw;
            return false;
        }
    }
}