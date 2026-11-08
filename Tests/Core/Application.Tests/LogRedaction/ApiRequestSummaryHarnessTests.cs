using Microsoft.AspNetCore.Http;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// The capture harness the log redaction tests stand on: the config-declared sink receives the bypass-routed request
/// summary of every request, once.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiRequestSummaryHarnessTests
{
    [Fact]
    public async Task Should_CaptureExactlyOneRequestSummaryPerRequest_When_TheApiServesRoutedAndUnroutedRequests()
    {
        await using var host = LogCaptureApiFactory.Start();
        var unrouted = $"/api/log-redaction-harness/{Guid.NewGuid():N}";

        await host.SendAsync(HttpMethods.Get, "/api/version");
        await host.SendAsync(HttpMethods.Get, unrouted);
        await host.SendSentinelAndAwaitItsSummaryAsync();

        var summaries = host.Events.Where(logEvent => logEvent.IsRequestSummary()).ToList();
        Assert.Multiple(
            () => Assert.Single(summaries, summary => summary.Mentions("/api/version")),
            () => Assert.Single(summaries, summary => summary.Mentions(unrouted)),
            () => Assert.Equal(3, summaries.Count));
    }
}