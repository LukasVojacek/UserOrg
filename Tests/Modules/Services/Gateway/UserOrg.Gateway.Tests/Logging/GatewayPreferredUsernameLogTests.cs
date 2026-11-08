using System.IdentityModel.Tokens.Jwt;

using Serilog.Events;

namespace UserOrg.Gateway.Tests.Logging;

public sealed class GatewayPreferredUsernameLogTests
{
    private const string PreferredUsername = "pu-marker";

    [Fact]
    public async Task Should_LeaveThePreferredUsernameOutOfEveryCapturedEvent_When_ATokenWithoutSubIsRejectedInDiagnosticMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));
        var token = GatewayLogHost.MintIdpToken(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.PreferredUsername] = PreferredUsername,
            [JwtRegisteredClaimNames.Jti] = "pu-token-jti",
        });

        await gateway.SendAsync(RouteSurfaceFixture.ServedV2Path, new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });

        Assert.Contains(gateway.Events, logEvent => logEvent.Level == LogEventLevel.Warning
            && logEvent.MessageTemplate.Text.StartsWith("[AUTH:GATEWAY] Token is missing required claims", StringComparison.Ordinal));
        Assert.Contains(gateway.Events, logEvent => logEvent.MessageTemplate.Text.StartsWith("[AUTH:GATEWAY] Claims (", StringComparison.Ordinal));
        gateway.AssertNoEventCarries(PreferredUsername);
    }
}