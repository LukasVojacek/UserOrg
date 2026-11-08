using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Authentication;
using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests.Logging;

public sealed class ServicesExtensionsTokenLogTests
{
    private const string Authority = "https://idp.test";
    private const string ActionLinkPath = "/api/v2/Public/ActionLinks/abc123";
    private const string AuthActivitySource = "UserOrg.Gateway.Auth";
    private const string PathTag = "auth.path";
    private const string ClaimMarker = "claim-marker@x";
    private const int IntrospectionPassedEventId = 16;

    [Fact]
    public async Task Should_LogTheTokenAcceptedEventAtDebug_When_ATokenWithAJtiIsValidated()
    {
        using var logs = new CapturingLoggerProvider();
        await using var services = Services(logs);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(TokenValidated(services));

        var accepted = Assert.Single(logs.From<Program>(), entry => entry.Message.StartsWith("[AUTH:GATEWAY] Token accepted with jti", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, accepted.Level);
    }

    [Fact]
    public async Task Should_KeepSubReadableAndRedactAnEmailClaim_When_TheClaimDumpIsLogged()
    {
        using var logs = new CapturingLoggerProvider();
        await using var services = Services(logs);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(TokenValidated(services));

        var dump = Assert.Single(logs.From<Program>(), entry => entry.Message.StartsWith("[AUTH:GATEWAY] Claims (", StringComparison.Ordinal));
        Assert.Contains("sub=sub-1", dump.Message, StringComparison.Ordinal);
        Assert.Contains("email=[REDACTED]", dump.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaimMarker, dump.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_TagTheMessageReceivedActivityWithoutThePath_When_ABearerMessageArrives()
    {
        using var logs = new CapturingLoggerProvider();
        await using var services = Services(logs);
        using var listener = ListenToAuthActivities(out var recorded);
        var httpContext = RequestTo(services, ActionLinkPath);
        var options = new JwtBearerOptions { Authority = Authority };

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnMessageReceived(new MessageReceivedContext(httpContext, BearerScheme(), options));

        var activity = Assert.Single(recorded, candidate => candidate.OperationName == AuthTelemetry.ActivityMessageReceived && candidate.GetTagItem("auth.scheme") is not null);
        Assert.Equal(Authority, activity.GetTagItem("auth.authority"));
        Assert.Null(activity.GetTagItem(PathTag));
    }

    [Fact]
    public async Task Should_TagTheAuthenticationFailedActivityWithoutThePath_When_ABearerFails()
    {
        using var logs = new CapturingLoggerProvider();
        await using var services = Services(logs);
        using var listener = ListenToAuthActivities(out var recorded);
        var httpContext = RequestTo(services, ActionLinkPath);
        var failed = new AuthenticationFailedContext(httpContext, BearerScheme(), new JwtBearerOptions())
        {
            Exception = new SecurityTokenMalformedException("malformed"),
        };

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnAuthenticationFailed(failed);

        var activity = Assert.Single(recorded, candidate => candidate.OperationName == AuthTelemetry.ActivityAuthenticationFailed && candidate.GetTagItem("auth.exception") is not null);
        Assert.Equal(nameof(SecurityTokenMalformedException), activity.GetTagItem("auth.exception"));
        Assert.Null(activity.GetTagItem(PathTag));
    }

    [Fact]
    public async Task Should_NotLogTheConfigurationManagerCheck_When_ABearerMessageArrives()
    {
        using var logs = new CapturingLoggerProvider();
        await using var services = Services(logs);
        var httpContext = RequestTo(services, ActionLinkPath);
        var options = new JwtBearerOptions { Authority = Authority };

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnMessageReceived(new MessageReceivedContext(httpContext, BearerScheme(), options));

        Assert.DoesNotContain(logs.From<Program>(), entry => entry.Message.Contains("ConfigurationManager", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_LogIntrospectionPassedAtDebug_When_TokenIsActive()
    {
        using var logs = new CapturingLoggerProvider();
        await using var services = Services(logs, new ActiveIntrospectionService());
        var context = TokenValidated(services);
        context.HttpContext.Items[ServicesExtensions.BearerTokenItemKey] = "Bearer opaque-token";

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        var passed = Assert.Single(logs.From<Program>(), entry => entry.Message.StartsWith("[AUTH:GATEWAY] Token introspection passed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, passed.Level);
        Assert.Equal(IntrospectionPassedEventId, passed.EventId.Id);
    }

    private static ServiceProvider Services(CapturingLoggerProvider logs, ITokenIntrospectionService? introspection = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        if (introspection is not null)
        {
            services.AddSingleton(introspection);
        }

        return services.BuildServiceProvider();
    }

    private static AuthenticationScheme BearerScheme() =>
        new(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));

    private static DefaultHttpContext RequestTo(IServiceProvider services, string path)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Path = path;
        httpContext.Request.Headers.Authorization = "Bearer not-a-jwt";
        return httpContext;
    }

    private static TokenValidatedContext TokenValidated(IServiceProvider services)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "sub-1"),
            new Claim(JwtRegisteredClaimNames.Jti, "jti-1"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "user-1"),
            new Claim(JwtRegisteredClaimNames.Email, ClaimMarker),
        ], "TestAuthType"));
        return new TokenValidatedContext(new DefaultHttpContext { RequestServices = services }, BearerScheme(), new JwtBearerOptions())
        {
            Principal = principal,
        };
    }

    private static ActivityListener ListenToAuthActivities(out List<Activity> recorded)
    {
        var activities = new List<Activity>();
        recorded = activities;
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AuthActivitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class ActiveIntrospectionService : ITokenIntrospectionService
    {
        public Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(new IntrospectionResult(true, null, new Dictionary<string, string[]>()));
    }
}
