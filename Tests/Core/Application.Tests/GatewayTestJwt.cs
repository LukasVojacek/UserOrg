using System.IdentityModel.Tokens.Jwt;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Mints HMAC-signed GatewayJwt tokens for <see cref="ProgramBootEnvironment.DefaultGatewayAudience"/> and makes the
/// booted host's <c>GatewayJwt</c> scheme accept them without reaching a metadata endpoint.
/// </summary>
internal static class GatewayTestJwt
{
    public static string Mint(SymmetricSecurityKey key, string gatewayAuthority, Dictionary<string, object> claims) =>
        new JsonWebTokenHandler().CreateToken(Descriptor(key, gatewayAuthority, claims));

    /// <summary>
    /// Writes the token with the gateway <c>TokenService</c>'s <see cref="JwtSecurityTokenHandler"/>, whose outbound
    /// claim map puts <see cref="System.Security.Claims.ClaimTypes.Role"/> on the wire under the short <c>role</c> name.
    /// </summary>
    public static string MintAsTheTokenServiceWritesIt(SymmetricSecurityKey key, string gatewayAuthority, Dictionary<string, object> claims)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(Descriptor(key, gatewayAuthority, claims)));
    }

    /// <summary>Validates <c>GatewayJwt</c> tokens against <paramref name="key"/>; <paramref name="configure"/> runs last.</summary>
    public static void Accept(
        IServiceCollection services,
        SymmetricSecurityKey key,
        string gatewayAuthority,
        Action<JwtBearerOptions>? configure = null) =>
        services.PostConfigure<JwtBearerOptions>("GatewayJwt", options =>
        {
            options.ConfigurationManager = null;
            options.TokenValidationParameters.IssuerSigningKey = key;
            options.TokenValidationParameters.ValidIssuer = gatewayAuthority;
            options.TokenValidationParameters.ValidAudience = ProgramBootEnvironment.DefaultGatewayAudience;
            configure?.Invoke(options);
        });

    private static SecurityTokenDescriptor Descriptor(SymmetricSecurityKey key, string gatewayAuthority, Dictionary<string, object> claims) =>
        new()
        {
            Issuer = gatewayAuthority,
            Audience = ProgramBootEnvironment.DefaultGatewayAudience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            NotBefore = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        };
}