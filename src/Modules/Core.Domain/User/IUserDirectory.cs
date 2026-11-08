namespace Core.Domain.User;

/// <summary>
/// Resolves a caller's domain <see cref="UserId"/> from their <see cref="IdpId"/> on the <b>write</b>
/// path only (e.g. to stamp <c>AssignedByUserId</c> / <c>CreatedByUserId</c>). ADR-0006 removed the
/// <c>IdpId → UserId</c> lookup from the read/scope path; writes still need it. Lightweight projection —
/// does not hydrate the full <see cref="User"/> aggregate.
/// </summary>
public interface IUserDirectory
{
    Task<UserId?> FindUserIdAsync(IdpId idpId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the <see cref="IdpId"/> for a domain <see cref="UserId"/> — the inverse direction, used on
    /// the write path to reach a referenced user's aggregate (keyed by <see cref="IdpId"/>) or to evict
    /// their cache. Lightweight projection; does not hydrate the full <see cref="User"/> aggregate.
    /// </summary>
    Task<IdpId?> FindIdpIdAsync(UserId userId, CancellationToken cancellationToken);
}