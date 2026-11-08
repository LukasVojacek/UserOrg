namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The ASP.NET Core and HttpClient spans carry the public token path redacted in url.path and url.full, and the
/// server span keeps OpenTelemetry's default query-value redaction.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class TokenPathTraceTests
{
    private const string UrlPath = "url.path";
    private const string UrlFull = "url.full";
    private const string UrlQuery = "url.query";

    private static readonly string[] UrlTags = [UrlPath, UrlFull];

    [Fact(DisplayName = "Should_CarryThePathInTheSpans_When_ThePathIsNoTokenRoute")]
    public async Task Should_CarryThePathInTheSpans_When_ThePathIsNoTokenRoute()
    {
        const string path = "/api/v2/Projects/visible-marker";
        await using var host = await RequestPipelineHost.StartAsync();

        var server = await host.ServerSpanOfAsync(path);
        var client = await host.HttpClientSpanOfAsync(path);

        Assert.Equal(path, Assert.Contains(UrlPath, server.Tags));
        Assert.Equal(RequestPipelineHost.DownstreamOrigin + path, Assert.Contains(UrlFull, client.Tags));
    }

    [Theory(DisplayName = "Should_CarryNoTokenInTheServerSpan_When_APublicTokenPathIsRequestedInMixedCase")]
    [MemberData(nameof(PublicTokenPaths.MixedCaseTokenPaths), MemberType = typeof(PublicTokenPaths))]
    public async Task Should_CarryNoTokenInTheServerSpan_When_APublicTokenPathIsRequestedInMixedCase(string path, string nonSecretPrefix)
    {
        await using var host = await RequestPipelineHost.StartAsync();

        var server = await host.ServerSpanOfAsync(path);

        Assert.StartsWith(nonSecretPrefix, Assert.Contains(UrlPath, server.Tags), StringComparison.Ordinal);
        AssertNoTokenIn(server);
    }

    [Theory(DisplayName = "Should_CarryNoTokenInTheHttpClientSpan_When_APublicTokenPathIsSentInMixedCase")]
    [MemberData(nameof(PublicTokenPaths.MixedCaseTokenPaths), MemberType = typeof(PublicTokenPaths))]
    public async Task Should_CarryNoTokenInTheHttpClientSpan_When_APublicTokenPathIsSentInMixedCase(string path, string nonSecretPrefix)
    {
        await using var host = await RequestPipelineHost.StartAsync();

        var client = await host.HttpClientSpanOfAsync(path);

        Assert.StartsWith(RequestPipelineHost.DownstreamOrigin + nonSecretPrefix, Assert.Contains(UrlFull, client.Tags), StringComparison.Ordinal);
        AssertNoTokenIn(client);
    }

    [Fact(DisplayName = "Should_CarryNoConsoleCredentialInTheServerSpanQuery_When_TheRoleManagementConsoleIsOpened")]
    public async Task Should_CarryNoConsoleCredentialInTheServerSpanQuery_When_TheRoleManagementConsoleIsOpened()
    {
        await using var host = await RequestPipelineHost.StartAsync();

        var server = await host.ServerSpanOfAsync("/role-management?t=secret");

        var query = Assert.Contains(UrlQuery, server.Tags);
        Assert.Contains("t=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", query, StringComparison.Ordinal);
    }

    private static void AssertNoTokenIn(CapturedActivity span)
    {
        foreach (var key in UrlTags)
        {
            if (span.Tags.TryGetValue(key, out var value))
            {
                Assert.DoesNotContain(PublicTokenPaths.Token, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}