using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Features.InternalToken;

namespace UserOrg.Gateway.Tests;

public sealed class KeyServiceJwksTests
{
    [Fact]
    public async Task GetCachedJwksJsonAsync_ReturnsJsonContainingKeys()
    {
        // Arrange
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

        // Act
        var jwks = await keyService.GetCachedJwksJsonAsync();

        // Assert
        Assert.NotNull(jwks);
        Assert.Contains("\"keys\"", jwks, StringComparison.OrdinalIgnoreCase);

        var kid = keyService.GetCurrentKeyId();
        Assert.False(string.IsNullOrEmpty(kid));
        Assert.Contains(kid, jwks, StringComparison.OrdinalIgnoreCase);

        // Cleanup
        await keyService.StopAsync(CancellationToken.None);
        keyService.Dispose();
    }

    [Fact]
    public void Should_PublishNoKeys_When_NoKeyHasBeenGenerated()
    {
        using var provider = BuildServices("ECDSA256");

        var jwks = provider.GetRequiredService<KeyService>().GetJsonWebKeySet();

        Assert.Empty(jwks.Keys);
    }

    [Fact]
    public async Task Should_PublishAnRs256Key_When_TheAlgorithmIsRsa()
    {
        using var provider = BuildServices("RSA2048");
        var keyService = provider.GetRequiredService<KeyService>();
        await keyService.StartAsync(TestContext.Current.CancellationToken);

        var jwk = Assert.Single(keyService.GetJsonWebKeySet().Keys);

        Assert.Equal(new[] { "RSA", "sig", keyService.GetCurrentKeyId(), "RS256" }, new[] { jwk.Kty, jwk.Use, jwk.Kid, jwk.Alg });
        Assert.Equal(256, Base64UrlEncoder.DecodeBytes(jwk.N).Length);
        Assert.Equal("AQAB", jwk.E);
        Assert.Null(jwk.Crv);
        Assert.Null(jwk.X);
    }

    [Theory]
    [InlineData("ECDSA256", "P-256", "ES256", 32)]
    [InlineData("ECDSA512", "P-521", "ES512", 66)]
    public async Task Should_PublishAnEcKeyOnTheConfiguredCurve_When_TheAlgorithmIsEcdsa(string algorithm, string curve, string jwsAlgorithm, int coordinateLength)
    {
        using var provider = BuildServices(algorithm);
        var keyService = provider.GetRequiredService<KeyService>();
        await keyService.StartAsync(TestContext.Current.CancellationToken);

        var jwk = Assert.Single(keyService.GetJsonWebKeySet().Keys);

        Assert.Equal(new[] { "EC", "sig", keyService.GetCurrentKeyId(), jwsAlgorithm, curve }, new[] { jwk.Kty, jwk.Use, jwk.Kid, jwk.Alg, jwk.Crv });
        Assert.Equal(coordinateLength, Base64UrlEncoder.DecodeBytes(jwk.X).Length);
        Assert.Equal(coordinateLength, Base64UrlEncoder.DecodeBytes(jwk.Y).Length);
        Assert.Null(jwk.N);
    }

    [Fact]
    public async Task Should_ListTheNewestKeyFirst_When_TheKeysHaveBeenRotated()
    {
        using var provider = BuildServices("ECDSA256");
        var keyService = provider.GetRequiredService<KeyService>();
        await keyService.StartAsync(TestContext.Current.CancellationToken);
        var firstKid = keyService.GetCurrentKeyId();

        keyService.ManualRotate();
        await WaitForAsync(() => keyService.GetJsonWebKeySet().Keys.Count == 2);

        var kids = keyService.GetJsonWebKeySet().Keys.Select(key => key.Kid).ToArray();
        Assert.Equal(new[] { keyService.GetCurrentKeyId(), firstKid }, kids);
        Assert.NotEqual(firstKid, keyService.GetCurrentKeyId());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static ServiceProvider BuildServices(string algorithm)
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton(typeof(Microsoft.Extensions.Caching.Hybrid.HybridCache), _ => new Microsoft.Extensions.Caching.Hybrid.ConcreteHybridCache());
        var settings = new Dictionary<string, string?>
        {
            ["InternalToken:Issuer"] = "https://test-issuer",
            ["InternalToken:Audience"] = "forge",
            ["InternalToken:KeyAlgorithm"] = algorithm,
        };
        services.AddInternalTokenFeature(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }
}
