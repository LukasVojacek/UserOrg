using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests.Logging;

public sealed class TokenServiceGeneratedJwtLogTests
{
    private const string PreferredUsername = "pu-marker";

    private static readonly Dictionary<string, string?> InternalTokenSettings = new()
    {
        ["InternalToken:Issuer"] = "https://test-issuer",
        ["InternalToken:Audience"] = "forge",
        ["InternalToken:ExpirationMinutes"] = "60",
        ["InternalToken:KeyAlgorithm"] = "ECDSA512",
    };

    [Fact]
    public async Task Should_LeaveThePreferredUsernameOutOfTheGeneratedJwtLine_When_AnInternalTokenIsMinted()
    {
        using var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddDistributedMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton<HybridCache>(_ => new ConcreteHybridCache());
        services.AddInternalTokenFeature(new ConfigurationBuilder().AddInMemoryCollection(InternalTokenSettings).Build());
        await using var provider = services.BuildServiceProvider();
        var keyService = provider.GetRequiredService<KeyService>();
        await keyService.StartAsync(TestContext.Current.CancellationToken);

        var token = await provider.GetRequiredService<TokenService>()
            .GenerateTokenAsync(EAuthorizationMethod.JwtBearerToken, "sub-1", PreferredUsername, "provider-1", "User", email: null);

        await keyService.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(token));
        var generated = Assert.Single(logs.From<TokenService>(), entry => entry.Message.StartsWith("Generated JWT", StringComparison.Ordinal));
        Assert.DoesNotContain(PreferredUsername, generated.Message, StringComparison.Ordinal);
    }
}