using System.IdentityModel.Tokens.Jwt;

using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Features.InternalToken;

namespace UserOrg.Gateway.Tests;

public sealed class TokenServiceHappyPathTests
{
    [Fact]
    public async Task GenerateServiceToServiceToken_HappyPath_CreatesValidJwt()
    {
        // Arrange - build DI container similar to application startup
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton(typeof(Microsoft.Extensions.Caching.Hybrid.HybridCache), sp => new Microsoft.Extensions.Caching.Hybrid.ConcreteHybridCache());

        var inMemory = new Dictionary<string, string?>
        {
            ["InternalToken:Issuer"] = "https://test-issuer",
            ["InternalToken:Audience"] = "forge",
            ["InternalToken:ExpirationMinutes"] = "60",
            ["InternalToken:KeyRotationIntervalHours"] = "24",
            ["InternalToken:KeyGracePeriodHours"] = "48",
            ["InternalToken:JwksCacheExpirationSeconds"] = "30",
            ["InternalToken:KeyAlgorithm"] = "ECDSA512"
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        services.AddInternalTokenFeature(configuration);

        using var provider = services.BuildServiceProvider();

        var keyService = provider.GetRequiredService<KeyService>();
        await keyService.StartAsync(CancellationToken.None);

        var tokenService = provider.GetRequiredService<TokenService>();

        // Act
        var token = await tokenService.GenerateServiceToServiceTokenAsync("myservice", "forge").ConfigureAwait(false) ?? "";

        // Assert
        Assert.False(string.IsNullOrEmpty(token));

        var signingCredentials = await keyService.GetSigningCredentialsAsync();

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["InternalToken:Issuer"],
            ValidAudience = "forge",
            IssuerSigningKey = signingCredentials.Key,
            ClockSkew = TimeSpan.FromSeconds(60)
        };

        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

        Assert.NotNull(principal);
        // Validate token payload directly to avoid mapping differences during ClaimsPrincipal creation
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal("service:myservice", jwt.Payload[JwtRegisteredClaimNames.Sub]?.ToString());
        Assert.Equal("myservice", jwt.Payload["origin_service"]?.ToString());

        // Cleanup
        await keyService.StopAsync(CancellationToken.None);
        keyService.Dispose();
    }
}