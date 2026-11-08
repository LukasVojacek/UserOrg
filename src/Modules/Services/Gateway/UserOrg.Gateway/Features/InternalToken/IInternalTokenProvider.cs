using System.Security.Claims;

using UserOrg.Shared;

namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Provides functionality for internal token generation and validation.
/// Used for inter-service communication with in-memory stored encryption certificates.
/// </summary>
internal interface IInternalTokenProvider
{
    /// <summary>
    /// Generates an internal JWT token for inter-service communication.
    /// The generated token will include standard claims (for example, <c>sub</c> for the subject and
    /// <c>name</c> for the username), the specified system role, optional email, and optional per-project roles.
    /// The token is signed using the current in-memory certificate and returned in compact serialized form.
    /// </summary>
    /// <param name="subject">The unique identifier for the token subject to be stored in the <c>sub</c> claim.</param>
    /// <param name="username">A human-readable username to include (commonly mapped to the <c>name</c> claim).</param>
    /// <param name="systemRole">The primary system-level role to include for the subject (emitted as a role claim).</param>
    /// <param name="email">Optional email address for the subject to include in the token (may be <c>null</c>).</param>
    /// <param name="projectRoles">Optional dictionary mapping project role names to collections of project short names.
    /// The dictionary key is the project role (for example "ProjectUser", "ProjectManager") and the value is a
    /// list of project short names in which the user has that role. Example:
    /// <code>{ "ProjectUser": ["projectA","projectC"], "ProjectManager": ["projectB"] }</code>
    /// Absence of a project means the user has no access to that project.</param>
    /// <param name="originalJti">The <c>jti</c> of the token that authenticated the caller, relayed verbatim into the
    /// <c>original_jti</c> claim and part of the token cache key; <c>null</c> when the caller arrived without a token
    /// (ApiKey, Cookie, public), in which case the minted token's own <c>jti</c> is the chain value.</param>
    /// <param name="providerName">The additive <c>providerName</c> claim (ADR-0011): the vault-resolved name for a
    /// provider service key, or, since D5 (ADR-0094), the DB-resolved name of the provider an interactive
    /// ProviderAdmin still administers; <c>null</c> when neither applies. Not part of the token cache key —
    /// see the cache-key comment in <see cref="InternalTokenProvider.GenerateInternalTokenAsync"/> for the
    /// staleness bound that leaves.</param>
    /// <returns>
    /// A <see cref="Task{String}"/> that resolves to the signed JWT as a compact serialized string.
    /// The returned token is suitable for use in Authorization headers for internal service-to-service requests.
    /// </returns>
    Task<string?> GenerateInternalTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string systemRole, string? providerId, string? email = null, IDictionary<string, ICollection<string>>? projectRoles = null, string? providerName = null, string? originalJti = null);

    /// <summary>
    /// Generates a service-to-service JWT token for internal inter-service communication.
    /// The token includes the origin service claim and is designed for short-lived S2S authentication.
    /// </summary>
    /// <param name="originService">The name of the calling service (e.g., "userorg", "gateway")</param>
    /// <param name="targetAudience">The target audience for the token (e.g., "forge")</param>
    /// <returns>
    /// A <see cref="Task{String}"/> that resolves to the signed JWT as a compact serialized string.
    /// The returned token contains the <c>origin_service</c> claim and is suitable for S2S authorization headers.
    /// </returns>
    Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience = "forge");

    /// <summary>
    /// Validates an internal JWT token.
    /// </summary>
    /// <param name="token">The JWT token to validate</param>
    /// <returns>ClaimsPrincipal if valid, null otherwise</returns>
    Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token);

    /// <summary>
    /// Gets the current key ID used for token signing.
    /// </summary>
    /// <returns>The current key ID</returns>
    string GetCurrentKeyId();
}