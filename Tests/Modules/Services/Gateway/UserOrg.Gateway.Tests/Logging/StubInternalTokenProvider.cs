using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests.Logging;

internal sealed class StubInternalTokenProvider(bool tokensAvailable) : IInternalTokenProvider
{
    private static readonly string Token =
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: [new Claim(JwtRegisteredClaimNames.Jti, "harness-s2s-jti")]));

    public Task<string?> GenerateInternalTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string systemRole, string? providerId, string? email = null, IDictionary<string, ICollection<string>>? projectRoles = null, string? providerName = null, string? originalJti = null)
        => Task.FromResult(tokensAvailable ? Token : null);

    public Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience = "forge")
        => Task.FromResult(tokensAvailable ? Token : null);

    public Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token) => Task.FromResult<ClaimsPrincipal?>(null);

    public string GetCurrentKeyId() => "harness-kid";
}