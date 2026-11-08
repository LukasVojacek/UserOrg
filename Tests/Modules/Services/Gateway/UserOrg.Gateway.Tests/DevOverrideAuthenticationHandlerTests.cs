using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Authentication;
using UserOrg.Gateway.Configuration;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests;

public sealed class DevOverrideAuthenticationHandlerTests
{
    private const string BearerIdpId = "idp-user-123";

    [Fact]
    public async Task Should_Authenticate_UsingBearerAsSub_When_Enabled()
    {
        var handler = await CreateHandlerAsync(
            new DevAuthOverrideOptions { Enabled = true },
            authHeader: $"Bearer {BearerIdpId}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var principal = result.Principal!;
        // The bearer string itself is the impersonated idpid.
        Assert.Equal(BearerIdpId, principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        // preferred_username mirrors the sub so the legacy user-init filter resolves the same user.
        Assert.Equal(BearerIdpId, principal.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername));
        // email is synthesized from the sub so JIT provisioning is exercisable locally (ADR-0068 §7).
        Assert.Equal($"{BearerIdpId}@dev.local", principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        // am = JwtBearerToken is what makes the downstream transform resolve roles from the DB.
        Assert.Equal(nameof(EAuthorizationMethod.JwtBearerToken), principal.FindFirstValue(EAuthorizationMethod.ClaimType));
        // No role is fabricated — the transform overwrites it from the DB snapshot.
        Assert.Null(principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Should_NormalizeSub_When_BearerCarriesAtSuffix()
    {
        const string BearerIdpIdWithAtSuffix = "abc@issuer";
        var handler = await CreateHandlerAsync(
            new DevAuthOverrideOptions { Enabled = true },
            authHeader: $"Bearer {BearerIdpIdWithAtSuffix}");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var principal = result.Principal!;
        var emailClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
        // The idpid is stored without the '@' suffix (UserInitializationEndpointFilter.cs:36), so the
        // synthesized address must normalize the same way, or Core.Domain's Email would reject it outright.
        Assert.Equal("abc@dev.local", emailClaim);
        Assert.Matches(ValidEmailFormat, emailClaim!);
    }

    // Local copy of Core.Domain's Email format expectation (Core.Shared.Email.EmailRegex) —
    // this test project must not reference Core.Domain to assert the claim's shape.
    private static readonly Regex ValidEmailFormat =
        new(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData(null)]            // no Authorization header at all
    [InlineData("")]             // empty header
    [InlineData("Bearer ")]      // bearer prefix with no token
    [InlineData("Bearer")]       // malformed
    public async Task Should_NotAuthenticate_When_BearerMissingOrEmpty(string? authHeader)
    {
        var handler = await CreateHandlerAsync(
            new DevAuthOverrideOptions { Enabled = true },
            authHeader);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Should_NotAuthenticate_When_Disabled()
    {
        var handler = await CreateHandlerAsync(
            new DevAuthOverrideOptions { Enabled = false },
            authHeader: $"Bearer {BearerIdpId}");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }

    private static async Task<DevOverrideAuthenticationHandler> CreateHandlerAsync(
        DevAuthOverrideOptions overrideOptions,
        string? authHeader)
    {
        var schemeOptions = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Setup(m => m.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var overrideMonitor = new Mock<IOptionsMonitor<DevAuthOverrideOptions>>();
        overrideMonitor.SetupGet(m => m.CurrentValue).Returns(overrideOptions);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton(schemeOptions.Object);
        services.AddSingleton(overrideMonitor.Object);
        services.AddSingleton(UrlEncoder.Default);
        var serviceProvider = services.BuildServiceProvider();

        var handler = ActivatorUtilities.CreateInstance<DevOverrideAuthenticationHandler>(serviceProvider);

        var context = new DefaultHttpContext { RequestServices = serviceProvider };
        if (authHeader is not null)
        {
            context.Request.Headers.Authorization = authHeader;
        }

        var scheme = new AuthenticationScheme(
            Constants.DevOverrideSchemeId, displayName: null, typeof(DevOverrideAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return handler;
    }
}
