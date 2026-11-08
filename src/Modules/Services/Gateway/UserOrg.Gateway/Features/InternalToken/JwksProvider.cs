using Microsoft.IdentityModel.Tokens;

namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Default implementation of IJwksProvider.
/// Manages JWKS by delegating to KeyService.
/// </summary>
internal sealed class JwksProvider(KeyService keyService) : IJwksProvider
{
    private readonly KeyService _keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));

    public Task<string> GetCachedJwksJsonAsync()
        => _keyService.GetCachedJwksJsonAsync();

    public JsonWebKeySet GetJsonWebKeySet()
        => _keyService.GetJsonWebKeySet();
}