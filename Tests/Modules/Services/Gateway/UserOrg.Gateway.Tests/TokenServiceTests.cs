using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

public sealed class TokenServiceTests
{
    // Shaped like the platform example in ADR 0092: the minting component's own prefix is part of the
    // value, so a token relayed verbatim proves the Gateway neither strips nor re-prefixes it.
    private const string IncomingJti = "onrtro:aaa95cb6-93ce-66da-c72f-f90d51837449";

    private static Task<string?> GenerateUserTokenAsync(TokenService tokenService, string? originalJti)
        => tokenService.GenerateTokenAsync(
            EAuthorizationMethod.JwtBearerToken,
            "sub-1",
            "user-1",
            "provider-1",
            "User",
            email: null,
            projectRoles: null,
            originalJti: originalJti);

    [Fact]
    public async Task GenerateTokenAsync_WithOriginalJti_RelaysItVerbatimAndKeepsItsOwnFreshJti()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var tokenService = services.GetRequiredService<TokenService>();

        var first = MintedToken.Read(await GenerateUserTokenAsync(tokenService, IncomingJti));
        var second = MintedToken.Read(await GenerateUserTokenAsync(tokenService, IncomingJti));

        Assert.Equal(IncomingJti, MintedToken.OriginalJtiOf(first));
        Assert.Equal(IncomingJti, MintedToken.OriginalJtiOf(second));
        Assert.NotEqual(IncomingJti, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task GenerateTokenAsync_WithoutOriginalJti_OmitsTheOriginalJtiClaim()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var tokenService = services.GetRequiredService<TokenService>();

        var token = MintedToken.Read(await GenerateUserTokenAsync(tokenService, originalJti: null));

        Assert.Null(MintedToken.OriginalJtiOf(token));
        Assert.False(string.IsNullOrEmpty(token.Id));
    }

    [Theory]
    [InlineData(null, "user-1", "User", "subject")]
    [InlineData("", "user-1", "User", "subject")]
    [InlineData("sub-1", null, "User", "username")]
    [InlineData("sub-1", "", "User", "username")]
    [InlineData("sub-1", "user-1", null, "role")]
    [InlineData("sub-1", "user-1", "", "role")]
    public async Task Should_ReturnNullWithAWarning_When_ARequiredValueIsMissing(string? subject, string? username, string? role, string missing)
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var tokenService = LoggedTokenService(services, loggerFactory);

        var token = await tokenService.GenerateTokenAsync(EAuthorizationMethod.JwtBearerToken, subject!, username!, "provider-1", role!, email: null);

        Assert.Null(token);
        var warning = Assert.Single(logs.All);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal($"GenerateTokenAsync called with empty or null {missing}", warning.Message);
    }

    [Fact]
    public async Task Should_LogTheKeyIdAndLifetimeAtDebug_When_ATokenIsMinted()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var tokenService = LoggedTokenService(services, loggerFactory);
        var kid = services.GetRequiredService<KeyService>().GetCurrentKeyId();

        await GenerateUserTokenAsync(tokenService, originalJti: null);

        var generated = Assert.Single(logs.All);
        Assert.Equal(LogLevel.Debug, generated.Level);
        Assert.Equal($"Generated JWT with kid {kid}, expires in 60 minutes", generated.Message);
    }

    [Fact]
    public async Task Should_CarryTheOptionalClaims_When_EmailProviderNameAndProjectRolesAreGiven()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var tokenService = services.GetRequiredService<TokenService>();
        var projectRoles = new Dictionary<string, ICollection<string>>
        {
            ["ProjectUser"] = ["alpha", "beta"],
            ["ProjectManager"] = null!,
        };

        var token = await tokenService.GenerateTokenAsync(
            EAuthorizationMethod.ApiKey, "sub-1", "user-1", "provider-1", "Admin", "user-1@test.local", projectRoles, providerName: "Provider One");

        using var payload = Payload(token);
        var root = payload.RootElement;
        Assert.Equal("sub-1", root.GetProperty("sub").GetString());
        Assert.Equal("user-1", root.GetProperty("preferred_username").GetString());
        Assert.Equal("ApiKey", root.GetProperty("am").GetString());
        Assert.Equal("provider-1", root.GetProperty("provider_id").GetString());
        Assert.Equal("Provider One", root.GetProperty("provider_name").GetString());
        Assert.Equal("user-1@test.local", root.GetProperty("email").GetString());
        Assert.Equal("https://test-issuer", root.GetProperty("iss").GetString());
        Assert.Equal("forge", root.GetProperty("aud").GetString());
        Assert.Equal("""{"ProjectUser":["alpha","beta"],"ProjectManager":[]}""", root.GetProperty("projectRoles").GetRawText());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Should_OmitTheOptionalClaims_When_EmailProviderNameAndProjectRolesAreAbsent(string? absent)
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var tokenService = services.GetRequiredService<TokenService>();

        var token = await tokenService.GenerateTokenAsync(
            EAuthorizationMethod.JwtBearerToken, "sub-1", "user-1", string.Empty, "User", absent, new Dictionary<string, ICollection<string>>(), providerName: absent);

        using var payload = Payload(token);
        var root = payload.RootElement;
        Assert.False(root.TryGetProperty("email", out _));
        Assert.False(root.TryGetProperty("provider_name", out _));
        Assert.False(root.TryGetProperty("projectRoles", out _));
        Assert.Equal(string.Empty, root.GetProperty("provider_id").GetString());
        Assert.Equal("JwtBearerToken", root.GetProperty("am").GetString());
    }

    private static TokenService LoggedTokenService(ServiceProvider services, ILoggerFactory loggerFactory) =>
        new(services.GetRequiredService<KeyService>(), services.GetRequiredService<InternalTokenOptions>(), loggerFactory.CreateLogger<TokenService>());

    private static JsonDocument Payload(string? token)
    {
        var jwt = MintedToken.Read(token);
        return JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.RawPayload));
    }
}
