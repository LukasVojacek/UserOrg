using Microsoft.AspNetCore.Http;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// In diagnostic mode a failing GatewayJwt on a public action-link route logs the API <c>[AUTH]</c> events at Debug
/// and Warning; the action-link token in the path is a credential and must reach none of them, nor any other event.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiDiagnosticAuthLogTokenPathTests
{
    private const string Token = "abc123";

    public static TheoryData<string> ActionLinkPaths =>
    [
        "/api/v2/Public/ActionLinks/abc123",
        "/API/V2/public/actionlinks/abc123",
        "/api/v2/PUBLIC/ActionLINKS/abc123",
    ];

    [Fact]
    public async Task Should_CaptureTheMessageReceivedAndValidationFailedAuthEvents_When_AFailingGatewayJwtArrivesInDiagnosticMode()
    {
        await using var host = LogCaptureApiFactory.Start(diagnosticMode: true);

        await host.SendAsync(HttpMethods.Post, "/api/v2/Public/ActionLinks/abc123", LogCaptureApiFactory.MintForeignToken());
        await host.SendSentinelAndAwaitItsSummaryAsync();

        Assert.Multiple(
            () => Assert.Contains(host.Events, logEvent => logEvent.HasTemplatePrefix("[AUTH] Message received")),
            () => Assert.Contains(host.Events, logEvent => logEvent.HasTemplatePrefix("[AUTH] Gateway JWT validation failed")));
    }

    [Theory]
    [MemberData(nameof(ActionLinkPaths))]
    public async Task Should_KeepTheActionLinkTokenOutOfTheAuthEvents_When_AFailingGatewayJwtArrivesInDiagnosticMode(string path)
    {
        await using var host = LogCaptureApiFactory.Start(diagnosticMode: true);

        await host.SendAsync(HttpMethods.Post, path, LogCaptureApiFactory.MintForeignToken());
        await host.SendSentinelAndAwaitItsSummaryAsync();

        var authEvents = host.Events.Where(logEvent => logEvent.HasTemplatePrefix("[AUTH]")).ToList();
        Assert.NotEmpty(authEvents);
        authEvents.AssertNoneMentions(Token);
    }

    [Theory]
    [MemberData(nameof(ActionLinkPaths))]
    public async Task Should_KeepTheActionLinkTokenOutOfEveryCapturedEvent_When_AFailingGatewayJwtArrivesInDiagnosticMode(string path)
    {
        await using var host = LogCaptureApiFactory.Start(diagnosticMode: true);

        await host.SendAsync(HttpMethods.Post, path, LogCaptureApiFactory.MintForeignToken());
        await host.SendSentinelAndAwaitItsSummaryAsync();

        Assert.Contains(host.Events, logEvent => logEvent.HasTemplatePrefix("[AUTH]"));
        host.Events.AssertNoneMentions(Token);
    }
}