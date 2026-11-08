using Microsoft.AspNetCore.Http;

using Serilog.Events;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// A valid GatewayJwt in diagnostic mode: the token-accepted event is Debug (ADR-0093 amendment; the summary's
/// <c>auth.jti</c> is the record of truth), and the claim dump prints every claim type outside the allow-list as
/// <c>[REDACTED]</c> while <c>sub</c> stays readable.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiTokenAcceptedLogTests
{
    private const string EmailMarker = "claim-marker@example.org";
    private const string AcceptedPrefix = "[AUTH] Token accepted with jti";
    private const string ClaimDumpPrefix = "[AUTH] Token validated for user";

    [Fact]
    public async Task Should_CaptureTheTokenAcceptedEventAndTheClaimDump_When_AValidGatewayJwtArrivesInDiagnosticMode()
    {
        var events = await AuthenticateAsync();

        Assert.Multiple(
            () => Assert.Single(events, logEvent => logEvent.HasTemplatePrefix(AcceptedPrefix)),
            () => Assert.Single(events, logEvent => logEvent.HasTemplatePrefix(ClaimDumpPrefix)));
    }

    [Fact]
    public async Task Should_LogTheTokenAcceptedEventAtDebug_When_AValidGatewayJwtArrives()
    {
        var events = await AuthenticateAsync();

        var accepted = Assert.Single(events, logEvent => logEvent.HasTemplatePrefix(AcceptedPrefix));
        Assert.Equal(LogEventLevel.Debug, accepted.Level);
    }

    [Fact]
    public async Task Should_RedactTheEmailClaimAndKeepSubReadable_When_TheClaimDumpIsLogged()
    {
        var events = await AuthenticateAsync();

        var dump = Assert.Single(events, logEvent => logEvent.HasTemplatePrefix(ClaimDumpPrefix));
        var claims = dump.StringProperty("Claims") ?? string.Empty;
        Assert.Multiple(
            () => Assert.DoesNotContain(EmailMarker, claims, StringComparison.OrdinalIgnoreCase),
            () => Assert.Contains("email=[REDACTED]", claims, StringComparison.Ordinal),
            () => Assert.Contains($"sub={LogCaptureApiFactory.Subject}", claims, StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<LogEvent>> AuthenticateAsync()
    {
        await using var host = LogCaptureApiFactory.Start(diagnosticMode: true);

        await host.SendAsync(HttpMethods.Get, "/api/version", LogCaptureApiFactory.MintGatewayToken(EmailMarker));
        await host.SendSentinelAndAwaitItsSummaryAsync();

        return host.Events;
    }
}