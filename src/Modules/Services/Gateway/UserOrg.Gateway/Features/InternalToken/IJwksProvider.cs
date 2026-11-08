using Microsoft.IdentityModel.Tokens;

namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Provides JWKS (JSON Web Key Set) functionality for token validation.
/// Used internally to expose public key information for token verification.
/// </summary>
internal interface IJwksProvider
{
    /// <summary>
    /// Gets the JSON Web Key Set in cached JSON format.
    /// </summary>
    /// <returns>JWKS JSON string</returns>
    Task<string> GetCachedJwksJsonAsync();

    /// <summary>
    /// Gets the complete JWKS object.
    /// </summary>
    /// <returns>JsonWebKeySet containing public keys</returns>
    JsonWebKeySet GetJsonWebKeySet();
}