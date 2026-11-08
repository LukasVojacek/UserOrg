namespace UserOrg.Gateway.Tests.Logging;

public sealed class GatewayTokenPathRedactionTests
{
    private const string Token = "abc123";
    private const string GatewayPathBase = "/userorg";

    public static TheoryData<string, bool> MixedCaseTokenPaths => new()
    {
        { "/api/v2/public/ACTIONLINKS/abc123", true },
        { "/API/v2/Public/Requests/byLink/abc123", true },
        { "/api/V2/public/ResourceRequestActions/abc123", true },
        { "/Api/public/HANDLE/abc123", true },
        { "/api/UserOrg/API/action/Handle/abc123", false },
    };

    public static TheoryData<string> TokenPrefixes =>
    [
        "/api/v2/Public/ActionLinks",
        "/api/v2/Public/Requests/ByLink",
        "/api/v2/Public/ResourceRequestActions",
        "/api/Public/Handle",
        "/api/Action/Handle",
    ];

    public static TheoryData<string, string> RedactedTokenPrefixes => new()
    {
        { "/api/v2/Public/ActionLinks", "/api/v2[REDACTED]" },
        { "/api/v2/Public/Requests/ByLink", "/api/v2[REDACTED]" },
        { "/api/v2/Public/ResourceRequestActions", "/api/v2[REDACTED]" },
        { "/api/Public/Handle", "/api[REDACTED]" },
        { "/api/Action/Handle", "/api[REDACTED]" },
    };

    [Fact]
    public async Task Should_CaptureTheSummaryCompletionAndYarpForwardOfAPath_When_ANonTokenPublicRouteIsProxiedInDiagnosticMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync($"/api/v2/Public/Glossary/{Token}");

        var carriers = gateway.EventsContaining(Token);
        Assert.Contains(carriers, GatewayLogHost.IsRequestSummary);
        Assert.Contains(carriers, GatewayLogHost.IsCompletionEvent);
        Assert.Contains(carriers, logEvent => GatewayLogHost.SourceContextOf(logEvent) == "Yarp.ReverseProxy.Forwarder.HttpForwarder");
    }

    [Theory]
    [MemberData(nameof(MixedCaseTokenPaths))]
    public async Task Should_LeaveThePathTokenOutOfEveryCapturedEvent_When_ATokenRouteIsRequestedInDiagnosticMode(string path, bool proxied)
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync(path);

        Assert.Equal(proxied ? [path] : [], gateway.UpstreamRequests);
        Assert.Contains(gateway.Events, GatewayLogHost.IsRequestSummary);
        gateway.AssertNoEventCarries(Token);
    }

    [Theory]
    [InlineData("?t=secret")]
    [InlineData("?T=secret")]
    [InlineData("?%74=secret")]
    [InlineData("?%54=secret")]
    public async Task Should_LeaveTheConsoleCredentialOutOfEveryCapturedEvent_When_RoleManagementIsRequestedWithItInDiagnosticMode(string query)
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync($"/role-management{query}");

        Assert.Contains(gateway.Events, logEvent => logEvent.MessageTemplate.Text.Contains("with invalid token", StringComparison.Ordinal));
        gateway.AssertNoEventCarries("secret");
    }

    [Fact]
    public async Task Should_CaptureTheQueryString_When_RoleManagementIsRequestedWithANonCredentialParameterInDiagnosticMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync("/role-management?q=visible-query-marker");

        Assert.Contains(gateway.EventsContaining("visible-query-marker"), GatewayLogHost.IsCompletionEvent);
    }

    [Fact]
    public async Task Should_CarryTheTokenInTheSummary_When_ANonTokenPublicRouteIsRequestedInNormalMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync();

        await gateway.SendAsync($"/api/v2/Public/Glossary/{Token}");

        Assert.Contains(gateway.EventsContaining(Token), GatewayLogHost.IsRequestSummary);
    }

    [Theory]
    [MemberData(nameof(MixedCaseTokenPaths))]
    public async Task Should_LeaveThePathTokenOutOfEveryCapturedEvent_When_ATokenRouteIsRequestedInNormalMode(string path, bool proxied)
    {
        await using var gateway = await GatewayLogHost.StartAsync();

        await gateway.SendAsync(path);

        Assert.Equal(proxied ? [path] : [], gateway.UpstreamRequests);
        gateway.AssertNoEventCarries(Token);
    }

    [Fact]
    public async Task Should_FlushNoYarpForward_When_AnErrorFollowsAForwardedTokenRequestInNormalMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync();
        Assert.Equal(Serilog.Events.LogEventLevel.Warning, gateway.Controller.LevelSwitch.MinimumLevel);

        await gateway.SendAsync(
            $"/api/v2/Public/ActionLinks/{Token}?page=visible-query-marker",
            new Dictionary<string, string> { [GatewayLogHost.ErrorAfterResponseHeader] = "1" });

        var yarp = gateway.Events
            .Where(logEvent => GatewayLogHost.SourceContextOf(logEvent)?.StartsWith("Yarp.ReverseProxy", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Contains(gateway.Events, logEvent => GatewayLogHost.SourceContextOf(logEvent) == "UserOrg.Gateway.Tests.Harness" && logEvent.Level == Serilog.Events.LogEventLevel.Error);
        Assert.Empty(yarp);
        gateway.AssertNoEventCarries(Token);
    }

    [Theory]
    [MemberData(nameof(RedactedTokenPrefixes))]
    public async Task Should_WriteOneSummaryPerRequestWithTheTokenRedacted_When_TheGatewayRunsUnderAPathBase(string prefix, string redactedPath)
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(PathBase: GatewayPathBase));

        await gateway.SendAsync($"{GatewayPathBase}{prefix.ToUpperInvariant()}/{Token}");
        await gateway.SendAsync($"{GatewayPathBase}{prefix}/{Token}");

        var summaries = gateway.RequestSummaries;
        Assert.Single(summaries, summary => GatewayLogHost.ScalarOf(summary, "Path") == redactedPath.ToUpperInvariant());
        Assert.Single(summaries, summary => GatewayLogHost.ScalarOf(summary, "Path") == redactedPath);
        Assert.All(summaries, summary => Assert.DoesNotContain(Token, GatewayLogHost.Render(summary), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(TokenPrefixes))]
    public async Task Should_WriteTheSummaryForAPathThatOnlyExtendsThePrefix_When_TheGatewayRunsUnderAPathBase(string prefix)
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(PathBase: GatewayPathBase));

        await gateway.SendAsync($"{GatewayPathBase}{prefix}X");

        Assert.Single(gateway.RequestSummaries, summary =>
            GatewayLogHost.ScalarOf(summary, "Path")?.EndsWith($"{prefix}X", StringComparison.OrdinalIgnoreCase) == true);
    }
}