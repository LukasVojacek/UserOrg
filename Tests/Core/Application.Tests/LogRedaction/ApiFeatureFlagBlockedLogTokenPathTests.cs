using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;

using Moq;

using Serilog.Events;

using UserOrg.Shared.FeatureManagement;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// The v1 routes are mapped when V1Api is on at start; turning the flag off at runtime makes FeatureFlagMiddleware
/// answer 404 and log the blocked path. The v1 action-link token in that path must not reach the line.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiFeatureFlagBlockedLogTokenPathTests
{
    private const string Token = "abc123";
    private const string BlockedLinePrefix = "Endpoint blocked by disabled feature flag";

    public static TheoryData<string> HandlePaths =>
    [
        "/api/Public/Handle/abc123",
        "/API/public/HANDLE/abc123",
    ];

    [Fact]
    public async Task Should_LogTheBlockedLine_When_V1ApiIsTurnedOffAfterStartAndAV1HandleRequestArrives()
    {
        await using var host = StartWithV1ApiOff();

        var context = await host.SendAsync(HttpMethods.Post, "/api/Public/Handle/abc123");
        await host.SendSentinelAndAwaitItsSummaryAsync();

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Single(BlockedLines(host.Events));
    }

    [Theory]
    [MemberData(nameof(HandlePaths))]
    public async Task Should_KeepTheActionLinkTokenOutOfTheBlockedLine_When_V1ApiIsOffAndAV1HandleRequestArrives(string path)
    {
        await using var host = StartWithV1ApiOff();

        await host.SendAsync(HttpMethods.Post, path);
        await host.SendSentinelAndAwaitItsSummaryAsync();

        var line = Assert.Single(BlockedLines(host.Events));
        Assert.False(line.Mentions(Token), line.Describe());
    }

    private static LogCaptureApiFactory StartWithV1ApiOff() =>
        LogCaptureApiFactory.Start(diagnosticMode: true, replaceServices: services =>
        {
            var features = new Mock<IFeatureManager>();
            features.Setup(manager => manager.IsEnabledAsync(FeatureFlags.V1Api)).ReturnsAsync(false);
            services.RemoveAll<IFeatureManager>();
            services.AddSingleton(features.Object);
        });

    private static List<LogEvent> BlockedLines(IEnumerable<LogEvent> events) =>
        [.. events.Where(logEvent => logEvent.IsFrom("FeatureFlagMiddleware") && logEvent.HasTemplatePrefix(BlockedLinePrefix))];
}