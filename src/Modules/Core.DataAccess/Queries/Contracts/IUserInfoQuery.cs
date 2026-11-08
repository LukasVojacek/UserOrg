namespace Core.DataAccess.Queries.Contracts;

public interface IUserInfoQuery
{
    Task<UserDetails?> GetBySubAsync(IdpId IdentityProviderId, CancellationToken ct);
    Task<bool> IsDeactivatedAsync(IdpId identityProviderId, CancellationToken ct);
    Task<UserExtendedDetails?> GetExtendedBySubAsync(IdpId identityProviderId, CancellationToken ct);
    Task<QueryPage<UserSummary>> GetUsersAsync(UserFilter filter, int page, int limit, CancellationToken ct);
    Task<List<UserWithIdSummary>> GetUsersAsync(CancellationToken ct);
    Task<string> GetUserPreferencesAsync(IdpId idpId, CancellationToken ct);
}