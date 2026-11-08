namespace Forge.Core.Features.UserOrgRoles;

/// <summary>
/// Resolves a provider service-key's surrogate id (the vault <c>Data</c> Guid, surfaced as the
/// <c>provider_id</c> claim) to the provider's natural name. The Gateway uses this to mint an additive
/// <c>providerName</c> claim on the internal token (ADR-0011). Results are cached.
/// </summary>
public interface IProviderIdentityResolver
{
    /// <summary>Returns the provider name for the given id, or null when no provider matches.</summary>
    Task<string?> GetProviderNameAsync(Guid providerId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the name of the provider currently assigned to the interactive user with the given identity-provider
    /// subject, or null when they have none. Cached like the lookup above, so a revoked assignment keeps answering
    /// until the entry lapses.
    /// </summary>
    Task<string?> GetAssignedProviderNameAsync(string idpUserId, CancellationToken cancellationToken);
}