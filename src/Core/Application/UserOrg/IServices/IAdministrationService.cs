using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IAdministrationService
{
    Task<IList<UserInfoExtendedModel>> GetSyncDetails(Guid providerId, CancellationToken ct);
    Task PromoteToProviderAdmin(string idpUserId, Guid providerId, CancellationToken ct);
    Task ResetToPlatformUser(string idpUserId, CancellationToken ct);

    /// <summary>
    /// Clears every HybridCache entry held by this process.
    /// </summary>
    /// <remarks>
    /// This method invalidates all cached data in this Core API instance only.
    /// Caches outside HybridCache (OpenID Connect metadata, signing keys in authentication middleware)
    /// are unaffected, as are the caches of other processes such as the Gateway.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    Task ResetAllCachesAsync(CancellationToken ct);
}