using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins who owns the identity claims a caller could otherwise assert about itself on a Bearer request —
/// <c>am</c> (authorization method) and <c>provider_id</c>. The Gateway is the only component that knows how
/// the caller actually authenticated and which provider it may act for; Core keys its service-key-only
/// provider write gate (D9, <c>Caller.IsServiceKeyIdentity</c>) on the first and kill-switch authority
/// (<c>KillSwitchAdminHandler.CanManage</c>) on the second. An inbound IdP token asserting either about
/// itself must never reach a consumer.
/// </summary>
public sealed class ServicesExtensionsSelfAssertedClaimTests
{
    private const string Authority = "https://idp.test";
    private const string ForgedProviderId = "0f9d5a1c-7c2e-4a58-8f3b-2c1d9e6b4a70";
    private const string SubjectId = "human-1";
    private const string PreferredUsername = "jane-doe";

    [Fact]
    public async Task Should_StampGatewayAuthMethod_When_InboundTokenCarriesNone()
    {
        var context = BuildTokenValidatedContext(BuildPrincipal(inboundAuthMethod: null));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Equal(nameof(EAuthorizationMethod.JwtBearerToken), ReadAuthMethod(context.Principal!));
    }

    [Fact]
    public async Task Should_OverrideForgedAuthMethod_When_InboundTokenClaimsToBeAnApiKey()
    {
        var context = BuildTokenValidatedContext(BuildPrincipal(nameof(EAuthorizationMethod.ApiKey)));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Equal(nameof(EAuthorizationMethod.JwtBearerToken), ReadAuthMethod(context.Principal!));
    }

    /// <summary>
    /// Stronger than the read above: the forged claim must be gone, not merely outranked. A fix that only
    /// reordered the identities would leave a second <c>am</c> on the principal for any consumer that
    /// enumerates claims instead of taking the first.
    /// </summary>
    [Fact]
    public async Task Should_LeaveExactlyOneAuthMethodClaim_When_InboundTokenCarriesOne()
    {
        var context = BuildTokenValidatedContext(BuildPrincipal(nameof(EAuthorizationMethod.ApiKey)));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        var authMethods = context.Principal!.FindAll(EAuthorizationMethod.ClaimType).Select(c => c.Value);
        Assert.Equal([nameof(EAuthorizationMethod.JwtBearerToken)], authMethods);
    }

    /// <summary>
    /// The same rule for the claim that decides <i>which</i> provider a caller acts for. It is as
    /// caller-influenceable as <c>am</c>, and downstream it wins over the Gateway's own resolution: the
    /// transform resolves an incoming <c>provider_id</c> to a <c>provider_name</c> that overwrites the minted
    /// one, so a forged value would hand an interactive caller another provider's read scope.
    /// </summary>
    [Fact]
    public async Task Should_DiscardForgedProviderId_When_InboundTokenCarriesOne()
    {
        var context = BuildTokenValidatedContext(BuildPrincipal(inboundAuthMethod: null, ForgedProviderId));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Empty(context.Principal!.FindAll(AuthConstants.ProviderIdClaimType));
    }

    /// <summary>
    /// The strip discarding a forged claim is the interesting security signal — someone presented a value the
    /// Gateway does not trust. It must reach the log with enough to investigate: which claim, and whose token.
    /// </summary>
    [Fact]
    public async Task Should_LogStrippedClaim_When_ForgedProviderIdIsSuccessfullyDiscarded()
    {
        var logger = new RecordingLogger<Program>();
        var context = BuildTokenValidatedContext(BuildPrincipal(inboundAuthMethod: null, ForgedProviderId), logger);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Equal(1, CountStrippedClaimLogs(logger, AuthConstants.ProviderIdClaimType));
    }

    /// <summary>
    /// Same signal for the other self-asserted claim, so a fix that only logs one of the two types would be
    /// caught.
    /// </summary>
    [Fact]
    public async Task Should_LogStrippedClaim_When_ForgedAuthMethodIsSuccessfullyDiscarded()
    {
        var logger = new RecordingLogger<Program>();
        var context = BuildTokenValidatedContext(BuildPrincipal(nameof(EAuthorizationMethod.ApiKey)), logger);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Equal(1, CountStrippedClaimLogs(logger, EAuthorizationMethod.ClaimType));
    }

    /// <summary>
    /// Both claims stripped in the same request must each get their own log entry — a regression that logged
    /// only the last one, or logged one of them twice, must fail here.
    /// </summary>
    [Fact]
    public async Task Should_LogBothStrippedClaims_When_InboundTokenCarriesBoth()
    {
        var logger = new RecordingLogger<Program>();
        var context = BuildTokenValidatedContext(BuildPrincipal(nameof(EAuthorizationMethod.ApiKey), ForgedProviderId), logger);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Equal(1, CountStrippedClaimLogs(logger, EAuthorizationMethod.ClaimType));
        Assert.Equal(1, CountStrippedClaimLogs(logger, AuthConstants.ProviderIdClaimType));
    }

    /// <summary>
    /// The strip is a security primitive, so a failed removal must not pass silently: the forged value would
    /// stay readable for every consumer while the Gateway believed it had authority. No legitimate IdP token
    /// in this system carries either claim, so failing the whole token costs an honest caller nothing.
    /// </summary>
    [Fact]
    public async Task Should_FailTheToken_When_ASelfAssertedClaimSurvivesTheStrip()
    {
        var principal = new ClaimsPrincipal(new IdentityWithUnremovableClaims(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "human-1"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "human-1"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey))
        ]));
        var context = BuildTokenValidatedContext(principal);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.Single(context.Principal!.Identities);
    }

    private static string? ReadAuthMethod(ClaimsPrincipal principal)
        => principal.FindFirstValue(EAuthorizationMethod.ClaimType);

    private static int CountStrippedClaimLogs(RecordingLogger<Program> logger, string claimType) =>
        logger.Records.Count(r =>
            r.Level == LogLevel.Warning &&
            r.Message.Contains(claimType, StringComparison.Ordinal) &&
            r.Message.Contains(SubjectId, StringComparison.Ordinal));

    private static TokenValidatedContext BuildTokenValidatedContext(ClaimsPrincipal principal, ILogger<Program>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (logger is not null)
        {
            services.AddSingleton(logger);
        }
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = principal
        };
    }

    /// <summary>
    /// The shape the JwtBearer handler produces: one identity built from the validated IdP token, carrying
    /// whatever claims the IdP put in it. No <c>BearerToken</c> item is placed on the context, so
    /// introspection is skipped and the handler runs its claim-stamping path.
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal(string? inboundAuthMethod, string? inboundProviderId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, SubjectId),
            new(JwtRegisteredClaimNames.PreferredUsername, PreferredUsername)
        };
        if (inboundAuthMethod is not null)
        {
            claims.Add(new Claim(EAuthorizationMethod.ClaimType, inboundAuthMethod));
        }
        if (inboundProviderId is not null)
        {
            claims.Add(new Claim(AuthConstants.ProviderIdClaimType, inboundProviderId));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    /// <summary>
    /// An identity whose claims are readable but not removable — the shape a <see cref="ClaimsIdentity"/>
    /// backed by an external claims provider has, and the only way to reach the strip's failure branch.
    /// </summary>
    private sealed class IdentityWithUnremovableClaims(IEnumerable<Claim> claims) : ClaimsIdentity(claims, "TestAuthType")
    {
        public override bool TryRemoveClaim(Claim claim) => false;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    /// <summary>
    /// Captures the rendered log message so the assertion is a plain string check. Moq can proxy
    /// <c>ILogger&lt;Program&gt;</c> here — the Gateway grants InternalsVisibleTo to DynamicProxyGenAssembly2
    /// — but verifying a log call through it needs the <c>It.IsAnyType</c> overload dance and never yields
    /// the formatted text this assertion reads.
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, formatter(state, exception)));
    }
}
