using System.IdentityModel.Tokens.Jwt;
using System.Linq;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Reads the claims a minted internal token carries, without validating it.
/// </summary>
internal static class MintedToken
{
    public static JwtSecurityToken Read(string? token)
    {
        Assert.NotNull(token);
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    // The claim name is spelled out rather than taken from AuthConstants on purpose: it is a
    // cross-process wire contract, so a rename of the constant's value must fail these tests.
    public static string? OriginalJtiOf(JwtSecurityToken token)
        => token.Claims.SingleOrDefault(claim => claim.Type == "original_jti")?.Value;
}
