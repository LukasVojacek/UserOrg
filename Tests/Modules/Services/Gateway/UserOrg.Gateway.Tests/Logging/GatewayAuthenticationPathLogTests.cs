using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using Serilog.Events;

using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests.Logging;

public sealed class GatewayAuthenticationPathLogTests
{
    private const string Token = "abc123";
    private const string ActionLinkPath = "/api/v2/Public/ActionLinks/abc123";
    private const string EmailedLinkPath = "/api/userorg/api/Action/Handle/abc123";
    private const string AuthenticationFailedTemplate = "[AUTH:GATEWAY] Authentication failed for path";

    private static readonly Dictionary<string, string> BadBearer = new() { ["Authorization"] = "Bearer not-a-jwt" };

    [Fact]
    public async Task Should_LeaveThePathTokenOutOfEveryCapturedEvent_When_TheS2sTokenCannotBeMintedForAnActionLink()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(S2sTokensAvailable: false));

        await gateway.SendAsync(ActionLinkPath);

        Assert.Contains(gateway.Events, logEvent => logEvent.Level == LogEventLevel.Error && logEvent.MessageTemplate.Text.StartsWith("Failed to generate S2S token", StringComparison.Ordinal));
        gateway.AssertNoEventCarries(Token);
    }

    [Fact]
    public async Task Should_WriteNoRoleManagementPathCheck_When_AnActionLinkIsRequestedInDiagnosticMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true, S2sTokensAvailable: false));

        await gateway.SendAsync(ActionLinkPath);

        Assert.DoesNotContain(gateway.Events, IsRoleManagementPathCheck);
        gateway.AssertNoEventCarries(Token);
    }

    [Fact]
    public async Task Should_LeaveThePathTokenOutOfTheGatewayAuthEvents_When_ABadBearerReachesTheEmailedLinkInDiagnosticMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync(EmailedLinkPath, BadBearer);

        var authEvents = gateway.Events.Where(IsGatewayAuthEvent).ToList();
        Assert.Contains(authEvents, logEvent => logEvent.MessageTemplate.Text.StartsWith("[AUTH:GATEWAY] Message received", StringComparison.Ordinal));
        Assert.Contains(authEvents, logEvent => logEvent.MessageTemplate.Text.StartsWith(AuthenticationFailedTemplate, StringComparison.Ordinal));
        Assert.DoesNotContain(authEvents, logEvent => GatewayLogHost.Render(logEvent).Contains(Token, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_LeaveThePathTokenOutOfTheAuthenticationFailedLine_When_ABadBearerReachesTheEmailedLinkInNormalMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync();

        await gateway.SendAsync(EmailedLinkPath, BadBearer);

        var failed = Assert.Single(gateway.Events, IsAuthenticationFailedLine);
        Assert.DoesNotContain(Token, GatewayLogHost.Render(failed), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_LeaveThePathTokenOutOfTheAuthenticationFailedLine_When_ABadBearerFailsOnAnActionLinkInNormalMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync();
        await using var scope = gateway.Services.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        httpContext.Request.Path = ActionLinkPath;
        var scheme = new AuthenticationScheme(Constants.BearerSchemeId, Constants.BearerSchemeId, typeof(JwtBearerHandler));
        var failedContext = new AuthenticationFailedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Exception = new SecurityTokenMalformedException("IDX14100: JWT is not well formed"),
        };

        await ServicesExtensions.CreateJwtBearerEvents(GatewayLogHost.Authority).OnAuthenticationFailed(failedContext);

        var failed = Assert.Single(gateway.Events, IsAuthenticationFailedLine);
        Assert.DoesNotContain(Token, GatewayLogHost.Render(failed), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthenticationFailedLine(LogEvent logEvent) =>
        logEvent.MessageTemplate.Text.StartsWith(AuthenticationFailedTemplate, StringComparison.Ordinal);

    private static bool IsGatewayAuthEvent(LogEvent logEvent) =>
        logEvent.MessageTemplate.Text.StartsWith("[AUTH:GATEWAY]", StringComparison.Ordinal);

    private static bool IsRoleManagementPathCheck(LogEvent logEvent) =>
        logEvent.MessageTemplate.Text.StartsWith("RoleManagement path check", StringComparison.Ordinal);
}