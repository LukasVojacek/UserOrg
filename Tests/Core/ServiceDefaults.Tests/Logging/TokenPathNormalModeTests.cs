namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// In Normal mode a request on a public token route leaves its token in no exported event: not in a rendered
/// message and not in any property value, however deeply nested.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class TokenPathNormalModeTests
{
    private const string ControlPath = "/api/v2/Projects/visible-marker";

    [Fact(DisplayName = "Should_ExportThePathValue_When_ThePathIsNoTokenRoute")]
    public async Task Should_ExportThePathValue_When_ThePathIsNoTokenRoute()
    {
        await using var host = await RequestPipelineHost.StartAsync();

        await host.GetEachAsync($"/api/v2/Public/Unlisted/{PublicTokenPaths.Token}", ControlPath);

        await host.AwaitSummariesForAsync(ControlPath);
        Assert.Contains(host.Events, e => RequestPipelineHost.TextOf(e).Contains("visible-marker", StringComparison.Ordinal));
        Assert.Contains(host.Events, e => RequestPipelineHost.TextOf(e).Contains(PublicTokenPaths.Token, StringComparison.Ordinal));
    }

    [Theory(DisplayName = "Should_ExportNoEventCarryingTheToken_When_APublicTokenPathIsRequestedInNormalMode")]
    [MemberData(nameof(PublicTokenPaths.MixedCaseTokenPathsOnly), MemberType = typeof(PublicTokenPaths))]
    public async Task Should_ExportNoEventCarryingTheToken_When_APublicTokenPathIsRequestedInNormalMode(string path)
    {
        await using var host = await RequestPipelineHost.StartAsync();

        await host.GetEachAsync(path, ControlPath);

        await host.AwaitSummariesForAsync(ControlPath);
        Assert.DoesNotContain(host.Events, e => RequestPipelineHost.TextOf(e).Contains(PublicTokenPaths.Token, StringComparison.OrdinalIgnoreCase));
    }
}