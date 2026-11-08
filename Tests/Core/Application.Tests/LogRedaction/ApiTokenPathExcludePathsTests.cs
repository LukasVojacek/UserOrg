using Microsoft.AspNetCore.Http;

using Serilog.Events;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// A request below a public token route prefix, in any casing, writes its request summary like any other request, and
/// StepUpLogging 5.0.2 redacts the token in it (ADR 0112, QS-01, QS-04); no token-route ExcludePaths entry remains.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiTokenPathExcludePathsTests
{
    public static TheoryData<string, string> BelowPrefix => new()
    {
        { HttpMethods.Post, "/API/v2/public/actionlinks/abc123" },
        { HttpMethods.Get, "/api/V2/PUBLIC/Requests/ByLink/abc123" },
        { HttpMethods.Get, "/api/v2/Public/RESOURCEREQUESTACTIONS/abc123" },
        { HttpMethods.Post, "/api/PUBLIC/handle/abc123" },
        { HttpMethods.Post, "/api/ACTION/Handle/abc123" },
    };

    public static TheoryData<string> PrefixTextOnly =>
    [
        "/api/v2/Public/ActionLinksX",
        "/api/v2/Public/Requests/ByLinkX",
        "/api/v2/Public/ResourceRequestActionsX",
        "/api/Public/HandleX",
        "/api/Action/HandleX",
    ];

    [Theory]
    [MemberData(nameof(BelowPrefix))]
    public async Task Should_WriteOneRequestSummaryWithoutTheToken_When_ThePathLiesBelowATokenRoutePrefixInAnyCasing(string method, string path)
    {
        var (summaries, events) = await CountNewSummariesAsync(method, path);

        Assert.Equal(1, summaries);
        events.AssertNoneMentions("abc123");
    }

    [Theory]
    [MemberData(nameof(PrefixTextOnly))]
    public async Task Should_WriteTheRequestSummary_When_ThePathOnlyStartsWithTheTokenRoutePrefixText(string path)
    {
        var (summaries, _) = await CountNewSummariesAsync(HttpMethods.Get, path);

        Assert.Equal(1, summaries);
    }

    private static async Task<(int Summaries, IReadOnlyList<LogEvent> Events)> CountNewSummariesAsync(string method, string path)
    {
        await using var host = LogCaptureApiFactory.Start();
        await host.SendSentinelAndAwaitItsSummaryAsync();
        var before = host.Events.Count(logEvent => logEvent.IsRequestSummary());

        await host.SendAsync(method, path);
        await host.SendSentinelAndAwaitItsSummaryAsync();

        const int sentinelSummaries = 1;
        var events = host.Events;
        return (events.Count(logEvent => logEvent.IsRequestSummary()) - before - sentinelSummaries, events);
    }
}