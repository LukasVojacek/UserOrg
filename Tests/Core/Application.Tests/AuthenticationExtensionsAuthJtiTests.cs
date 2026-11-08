using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using OpenTelemetry;
using OpenTelemetry.Trace;

using Serilog;

using UserOrg.ServiceDefaults;
using UserOrg.Shared;

using UserOrganizationManagement.API.Authentication;

using GatewayJwtAuthenticationExtensions = UserOrganizationManagement.API.Authentication.AuthenticationExtensions;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Covers the <c>auth.jti</c> logging added to <see cref="GatewayJwtAuthenticationExtensions.CreateGatewayJwtBearerEvents"/>'s
/// <c>OnTokenValidated</c> handler: Core API only ever sees the Gateway-minted internal token, so the value
/// logged is <c>original_jti ?? jti</c> off that token's own claims (ADR 0092/0093) — matching whatever the
/// Gateway logged for the same request.
/// </summary>
public sealed class AuthenticationExtensionsAuthJtiTests
{
    private const string Authority = "https://gateway.test";

    [Fact]
    public async Task OnTokenValidated_WithOriginalJti_SetsDiagnosticContextAndActivityTagToTheOriginalJti()
    {
        var diagnosticContext = new Mock<IDiagnosticContext>();
        await using var app = BuildRealTracedApp(diagnosticContext.Object, out var recorded);

        var context = BuildTokenValidatedContext(app.Services, BuildPrincipal(jti: "internal-jti-1", originalJti: "idp-jti-1"));

        await GatewayJwtAuthenticationExtensions.CreateGatewayJwtBearerEvents(Authority).OnTokenValidated(context);

        diagnosticContext.Verify(d => d.Set(AuthTelemetry.TagJti, "idp-jti-1", false), Times.Once);
        var activity = Assert.Single(FindRecordedTokenValidatedActivities(recorded, "idp-jti-1"));
        Assert.Equal("idp-jti-1", activity.GetTagItem(AuthTelemetry.TagJti));
    }

    [Fact]
    public async Task OnTokenValidated_WithoutOriginalJti_FallsBackToTheInternalTokensOwnJti()
    {
        var diagnosticContext = new Mock<IDiagnosticContext>();
        await using var app = BuildRealTracedApp(diagnosticContext.Object, out var recorded);

        var context = BuildTokenValidatedContext(app.Services, BuildPrincipal(jti: "internal-jti-1", originalJti: null));

        await GatewayJwtAuthenticationExtensions.CreateGatewayJwtBearerEvents(Authority).OnTokenValidated(context);

        diagnosticContext.Verify(d => d.Set(AuthTelemetry.TagJti, "internal-jti-1", false), Times.Once);
        var activity = Assert.Single(FindRecordedTokenValidatedActivities(recorded, "internal-jti-1"));
        Assert.Equal("internal-jti-1", activity.GetTagItem(AuthTelemetry.TagJti));
    }

    /// <summary>
    /// A logging side-channel must never be able to fail an authenticated request. Verified with no
    /// <see cref="IDiagnosticContext"/> registered at all (e.g. a host that skips <c>AddServiceDefaults</c>),
    /// not merely a null one, so a <see cref="ServiceProviderServiceExtensions.GetRequiredService{T}"/>-shaped
    /// regression is caught here.
    /// </summary>
    [Fact]
    public async Task OnTokenValidated_WithNoDiagnosticContextRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var context = BuildTokenValidatedContext(provider, BuildPrincipal(jti: "internal-jti-1", originalJti: "idp-jti-1"));

        var exception = await Record.ExceptionAsync(
            () => GatewayJwtAuthenticationExtensions.CreateGatewayJwtBearerEvents(Authority).OnTokenValidated(context));

        Assert.Null(exception);
    }

    /// <summary>
    /// Builds a host through the real <see cref="Extensions.ConfigureOpenTelemetry{TBuilder}"/> wiring — not a
    /// test-supplied <see cref="ActivityListener"/> — so a missing <c>AddSource</c> registration for
    /// <see cref="AuthTelemetry.ActivitySourceName"/> fails this test the same way it fails in production
    /// (<c>StartActivity</c> returns null and every tag becomes a no-op).
    /// </summary>
    private static WebApplication BuildRealTracedApp(IDiagnosticContext diagnosticContext, out List<Activity> recorded)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "Application.Tests"
        });
        builder.ConfigureOpenTelemetry();

        var capturedActivities = new List<Activity>();
        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(new RecordingProcessor(capturedActivities)));
        builder.Services.AddSingleton(diagnosticContext);

        var app = builder.Build();
        app.Services.GetRequiredService<TracerProvider>(); // forces the provider (and its ActivityListener) to build
        recorded = capturedActivities;
        return app;
    }

    /// <summary>
    /// Activity sources are process-global, so xUnit's parallel test execution can hand this processor
    /// activities started by other tests exercising the same ActivitySource. Filtering by the jti tag value
    /// (unique per test call), not just operation name, keeps the assertion correct even under contamination.
    /// </summary>
    private static IEnumerable<Activity> FindRecordedTokenValidatedActivities(IEnumerable<Activity> recorded, string jti)
        => recorded.Where(a => a.OperationName == AuthTelemetry.ActivityTokenValidated && (string?)a.GetTagItem(AuthTelemetry.TagJti) == jti);

    private static TokenValidatedContext BuildTokenValidatedContext(IServiceProvider services, ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme("GatewayJwt", "GatewayJwt", typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = principal
        };
    }

    private static ClaimsPrincipal BuildPrincipal(string jti, string? originalJti)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "sub-1"),
            new(JwtRegisteredClaimNames.PreferredUsername, "user-1"),
            new(ClaimTypes.Role, "Provider"),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        if (originalJti is not null)
        {
            claims.Add(new Claim(AuthConstants.OriginalJtiClaimType, originalJti));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "GatewayJwt"));
    }

    private sealed class RecordingProcessor(List<Activity> recorded) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => recorded.Add(data);
    }
}
