using System.Collections.Concurrent;
using System.Diagnostics;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// A request under a public token route writes its request summary like any other request (ADR 0112, QS-01), and
/// StepUpLogging 5.0.2 redacts the token segment in the summary's Path, the completion event's RequestPath and the
/// LogRequest span's http.target (QS-04), so no token-route ExcludePaths entry is needed.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class TokenPathExclusionTests
{
    private const string ControlPath = "/api/v2/Projects/42";
    private const string Redacted = "[REDACTED]";

    [Fact(DisplayName = "Should_WriteExactlyOneRequestSummary_When_ThePathIsNoTokenRoute")]
    public async Task Should_WriteExactlyOneRequestSummary_When_ThePathIsNoTokenRoute()
    {
        await using var host = await RequestPipelineHost.StartAsync();

        using var response = await host.GetAsync(ControlPath);

        Assert.Single(await host.AwaitSummariesForAsync(ControlPath));
    }

    [Theory(DisplayName = "Should_WriteExactlyOneRedactedRequestSummary_When_ThePathIsUnderATokenPrefixInMixedCase")]
    [MemberData(nameof(PublicTokenPaths.Prefixes), MemberType = typeof(PublicTokenPaths))]
    public async Task Should_WriteExactlyOneRedactedRequestSummary_When_ThePathIsUnderATokenPrefixInMixedCase(string prefix)
    {
        await using var host = await RequestPipelineHost.StartAsync();
        var tokenPath = PublicTokenPaths.TokenPathUnder(prefix);

        await host.GetEachAsync(tokenPath, ControlPath);

        Assert.Single(await host.AwaitSummariesForAsync(ControlPath));
        var summary = Assert.Single(host.Events, e => RequestPipelineHost.IsRequestSummary(e) && ScalarOf(e, "Path") != ControlPath);
        AssertRedacted(ScalarOf(summary, "Path"));
    }

    [Theory(DisplayName = "Should_RedactTheTokenInRequestPathAndHttpTarget_When_ThePathIsUnderATokenPrefixInDiagnosticMode")]
    [MemberData(nameof(PublicTokenPaths.Prefixes), MemberType = typeof(PublicTokenPaths))]
    public async Task Should_RedactTheTokenInRequestPathAndHttpTarget_When_ThePathIsUnderATokenPrefixInDiagnosticMode(string prefix)
    {
        using var spans = new RequestSpanCapture();
        await using var host = await RequestPipelineHost.StartAsync(LoggingPipelineHost.Diagnostic());
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);
        var tokenPath = PublicTokenPaths.TokenPathUnder(prefix);

        await host.GetEachAsync(tokenPath, ControlPath);
        await host.AwaitSummariesForAsync(ControlPath);

        var requestPaths = host.Events.Select(e => ScalarOf(e, "RequestPath")).OfType<string>().Distinct().ToList();
        Assert.Contains(ControlPath, requestPaths);
        var tokenRequestPaths = requestPaths.Where(path => path != ControlPath).ToList();
        Assert.NotEmpty(tokenRequestPaths);
        Assert.All(tokenRequestPaths, AssertRedacted);

        await LoggingPipelineHost.WaitUntilAsync(() => spans.Targets.Count >= 2);
        Assert.Contains(ControlPath, spans.Targets);
        AssertRedacted(Assert.Single(spans.Targets, target => target != ControlPath));
    }

    [Theory(DisplayName = "Should_WriteARequestSummary_When_ThePathOnlyExtendsTheLastPrefixSegment")]
    [MemberData(nameof(PublicTokenPaths.Prefixes), MemberType = typeof(PublicTokenPaths))]
    public async Task Should_WriteARequestSummary_When_ThePathOnlyExtendsTheLastPrefixSegment(string prefix)
    {
        await using var host = await RequestPipelineHost.StartAsync();
        var sibling = PublicTokenPaths.SiblingOf(prefix);

        using var response = await host.GetAsync(sibling);

        Assert.Single(await host.AwaitSummariesForAsync(sibling));
    }

    private static void AssertRedacted(string? value)
    {
        Assert.NotNull(value);
        Assert.DoesNotContain(PublicTokenPaths.Token, value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Redacted, value, StringComparison.Ordinal);
    }

    private static string? ScalarOf(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text } ? text : null;

    private sealed class RequestSpanCapture : IDisposable
    {
        private readonly ConcurrentQueue<string> _targets = new();
        private readonly ActivityListener _listener;

        public RequestSpanCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Lukdrasil.StepUpLogging", StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.GetTagItem("http.target") is string target)
                    {
                        _targets.Enqueue(target);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<string> Targets => [.. _targets];

        public void Dispose() => _listener.Dispose();
    }
}