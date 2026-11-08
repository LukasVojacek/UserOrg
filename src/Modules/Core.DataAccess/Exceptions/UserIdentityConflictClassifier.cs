using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Exceptions;

/// <summary>
/// Classifies a <see cref="DbUpdateException"/> from a User insert as the "someone else provisioned this sub
/// first" conflict (ADR-0068 §6): two concurrent first logins for a brand-new sub both miss FindAsync and
/// both insert, so the loser's SaveChangesAsync must be recognised as that race, not surfaced as a generic
/// 500. Any other DbUpdateException is left for the caller to rethrow — mirrors the pattern in
/// ResourceRequestConflict.cs.
/// </summary>
public static class UserIdentityConflictClassifier
{
    public const string IdentityProviderIndexName = "UX_User_IdentityProviderUserId";
    public const string UserNameIndexName = "UX_User_UserName";

    /// <summary>
    /// Both racing inserts carry the same token, so the loser duplicates the winner on
    /// <see cref="IdentityProviderIndexName"/> AND <see cref="UserNameIndexName"/> (same preferred_username).
    /// SQL Server names only the first of the two unique indexes it happens to hit, and that order is an
    /// unasserted property of index creation - not something a message match should ever depend on. Matching
    /// either name keeps this classifier from silently stopping to fire if that order ever changes.
    /// </summary>
    public static bool IsIdentityProviderConflict(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains(IdentityProviderIndexName, StringComparison.Ordinal)
            || message.Contains(UserNameIndexName, StringComparison.Ordinal);
    }
}