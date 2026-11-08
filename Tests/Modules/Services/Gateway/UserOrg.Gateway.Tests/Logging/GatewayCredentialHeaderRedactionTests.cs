namespace UserOrg.Gateway.Tests.Logging;

public sealed class GatewayCredentialHeaderRedactionTests
{
    private const string ProxiedPath = "/api/v2/Version";

    public static TheoryData<string> CredentialHeaders =>
    [
        "Authorization",
        "X-Original-Authorization",
        "X-Api-ServiceToken",
        "X-Api-ProviderServiceKey",
        "X-Api-Token",
        "X-Api-Key",
    ];

    [Fact]
    public async Task Should_LogANonSensitiveHeaderVerbatimInTheCompletionEvent_When_AProxiedRequestCarriesItInDiagnosticMode()
    {
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync(ProxiedPath, new Dictionary<string, string> { ["X-Harness-Marker"] = "visible-header-marker" });

        Assert.Equal([ProxiedPath], gateway.UpstreamRequests);
        var completion = Assert.Single(gateway.Events, GatewayLogHost.IsCompletionEvent);
        Assert.Equal("visible-header-marker", GatewayLogHost.HeaderOf(completion, "X-Harness-Marker"));
    }

    [Theory]
    [MemberData(nameof(CredentialHeaders))]
    public async Task Should_RedactTheCredentialHeaderInTheCompletionEvent_When_AProxiedRequestCarriesItInDiagnosticMode(string header)
    {
        var value = $"{header}-value-7f3a";
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync(ProxiedPath, new Dictionary<string, string> { [header] = value });

        Assert.Equal([ProxiedPath], gateway.UpstreamRequests);
        var completion = Assert.Single(gateway.Events, GatewayLogHost.IsCompletionEvent);
        Assert.Equal("[REDACTED]", GatewayLogHost.HeaderOf(completion, header));
    }

    [Theory]
    [MemberData(nameof(CredentialHeaders))]
    public async Task Should_LeaveTheCredentialValueOutOfEveryCapturedEvent_When_AProxiedRequestCarriesItInDiagnosticMode(string header)
    {
        var value = $"{header}-value-7f3a";
        await using var gateway = await GatewayLogHost.StartAsync(new GatewayLogOptions(Diagnostic: true));

        await gateway.SendAsync(ProxiedPath, new Dictionary<string, string> { [header] = value });

        Assert.Equal([ProxiedPath], gateway.UpstreamRequests);
        Assert.Contains(gateway.Events, logEvent => GatewayLogHost.SourceContextOf(logEvent) == "Yarp.ReverseProxy.Forwarder.HttpForwarder");
        gateway.AssertNoEventCarries(value);
    }
}