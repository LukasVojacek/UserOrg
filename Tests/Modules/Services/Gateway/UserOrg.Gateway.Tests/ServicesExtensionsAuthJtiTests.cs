using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using UserOrg.Gateway.Startup;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Covers the <c>auth.jti</c> logging added to <see cref="ServicesExtensions.CreateJwtBearerEvents"/>'s
/// <c>OnTokenValidated</c> handler: the incoming token's own <c>jti</c> is the value both this process and
/// Core API log for a Bearer request (ADR 0093), sourced from the validated principal so it is set
/// regardless of what the request does afterwards (success, 401, 403, or a Cyclops forward).
/// </summary>
public sealed class ServicesExtensionsAuthJtiTests
{
    private const string Authority = "https://idp.test";

    [Fact]
    public async Task OnTokenValidated_WithJti_SetsDiagnosticContextAndActivityTag()
    {
        const string jti = "idp-jti-1";
        var diagnosticContext = new Mock<IDiagnosticContext>();
        using var provider = BuildServiceProvider(diagnosticContext.Object);
        using var listener = ListenToAuthActivities(out var recorded);

        var context = BuildTokenValidatedContext(provider, BuildPrincipal(jti));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        diagnosticContext.Verify(d => d.Set(AuthTelemetry.TagJti, jti, false), Times.Once);
        Assert.Single(FindRecordedTokenValidatedActivities(recorded, jti));
    }

    [Fact]
    public async Task OnTokenValidated_WithoutJti_SetsNeitherDiagnosticContextNorActivityTag()
    {
        var diagnosticContext = new Mock<IDiagnosticContext>();
        using var provider = BuildServiceProvider(diagnosticContext.Object);
        using var listener = ListenToAuthActivities(out var recorded);

        var context = BuildTokenValidatedContext(provider, BuildPrincipal(jti: null));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        diagnosticContext.Verify(d => d.Set(AuthTelemetry.TagJti, It.IsAny<object>(), It.IsAny<bool>()), Times.Never);
        var activity = Assert.Single(recorded, a => a.OperationName == AuthTelemetry.ActivityTokenValidated);
        Assert.Null(activity.GetTagItem(AuthTelemetry.TagJti));
    }

    [Fact]
    public async Task OnTokenValidated_WithEmptyJti_IsTreatedAsHavingNoJti()
    {
        var diagnosticContext = new Mock<IDiagnosticContext>();
        using var provider = BuildServiceProvider(diagnosticContext.Object);
        using var listener = ListenToAuthActivities(out _);

        var context = BuildTokenValidatedContext(provider, BuildPrincipal(jti: string.Empty));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        diagnosticContext.Verify(d => d.Set(AuthTelemetry.TagJti, It.IsAny<object>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// A logging side-channel must never be able to fail an authenticated request. Verified with no
    /// <see cref="IDiagnosticContext"/> registered at all (e.g. a host that skips <c>AddServiceDefaults</c>),
    /// not merely a null one, so a <see cref="GetRequiredService{T}"/>-shaped regression is caught here.
    /// </summary>
    [Fact]
    public async Task OnTokenValidated_WithNoDiagnosticContextRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        using var listener = ListenToAuthActivities(out var recorded);

        var context = BuildTokenValidatedContext(provider, BuildPrincipal("idp-jti-1"));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Single(FindRecordedTokenValidatedActivities(recorded, "idp-jti-1"));
    }

    private static ServiceProvider BuildServiceProvider(IDiagnosticContext diagnosticContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(diagnosticContext);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// <see cref="ActivitySource.AddActivityListener"/> registers process-globally, so xUnit's parallel test
    /// execution can hand this listener activities started by other tests exercising the same
    /// ActivitySource. Filtering by the jti tag value (unique per test call), not just operation name, keeps
    /// the assertion correct even under contamination.
    /// </summary>
    private static ActivityListener ListenToAuthActivities(out List<Activity> recorded)
    {
        var activities = new List<Activity>();
        recorded = activities;
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AuthTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static IEnumerable<Activity> FindRecordedTokenValidatedActivities(IEnumerable<Activity> recorded, string jti)
        => recorded.Where(a => a.OperationName == AuthTelemetry.ActivityTokenValidated && (string?)a.GetTagItem(AuthTelemetry.TagJti) == jti);

    private static TokenValidatedContext BuildTokenValidatedContext(IServiceProvider services, ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = principal
        };
    }

    private static ClaimsPrincipal BuildPrincipal(string? jti)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "sub-1"),
            new(JwtRegisteredClaimNames.PreferredUsername, "user-1")
        };
        if (jti is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, jti));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }
}
