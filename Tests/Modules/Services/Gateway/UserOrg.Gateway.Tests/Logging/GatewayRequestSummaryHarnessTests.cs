namespace UserOrg.Gateway.Tests.Logging;

public sealed class GatewayRequestSummaryHarnessTests
{
    private static readonly string[] Paths = ["/api/v2/Version", "/api/v2/Public/Glossary/summary-marker", "/api/v2/Projects/TESTPRJ"];

    [Fact]
    public async Task Should_CaptureExactlyOneRequestSummaryPerRequest_When_TheGatewayServesRequestsInNormalMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync();

        foreach (var path in Paths)
        {
            await gateway.SendAsync(path);
        }

        var summaries = gateway.RequestSummaries;
        Assert.True(summaries.Count == Paths.Length, $"expected {Paths.Length} request summaries, captured:{Environment.NewLine}{GatewayLogHost.Describe(gateway.Events)}");
        foreach (var path in Paths)
        {
            Assert.Single(summaries, summary => GatewayLogHost.ScalarOf(summary, "Path") == path);
        }
    }

    [Fact]
    public async Task Should_ForwardAPublicRequestToTheStubUpstream_When_TheS2sTokenIsMinted()
    {
        await using var gateway = await GatewayLogHost.StartAsync();

        var status = await gateway.SendAsync("/api/v2/Version");

        Assert.Equal(200, status);
        Assert.Equal(["/api/v2/Version"], gateway.UpstreamRequests);
    }
}