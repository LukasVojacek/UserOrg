using System.Net.Http;

using Microsoft.AspNetCore.Http;

using Yarp.ReverseProxy.Transforms;

using Harness = UserOrg.Gateway.Tests.CyclopsForwardingTestHarness;

namespace UserOrg.Gateway.Tests;

public sealed class CyclopsPermissionsChangedIsolationTests
{
    private const string ChangedHeader = "X-UserOrg-Permissions-Changed";
    private const string CapabilityHeader = "X-UserOrg-Gateway-Capabilities";

    public static TheoryData<string> Routes() => new() { "usage", "total-usage", "usage-v1" };

    private static Task<CyclopsTransformRun> ApplyAsync(string route, IServiceProvider services) => route switch
    {
        "usage" => Harness.ApplyUsageAsync(Harness.BuildHttpContext($"{Harness.UsagePath}proj-a/res-b", services)),
        "total-usage" => Harness.ApplyTotalUsageAsync(Harness.BuildHttpContext($"{Harness.TotalUsagePath}res-b", services)),
        "usage-v1" => Harness.ApplyUsageV1Async(Harness.BuildHttpContext($"{Harness.UsageV1Path}{Harness.V1ProjectShortName}/{Harness.V1ResourceName}", services)),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Should_NotAdvertiseTheCapability_When_ACyclopsForwarderBuildsItsRequest(string route)
    {
        using var services = Harness.BuildServices();

        var run = await ApplyAsync(route, services);

        Assert.NotNull(run.RequestUri);
        Assert.False(run.ProxyRequest.Headers.Contains(CapabilityHeader));
        Assert.Equal(2, run.BuilderContext.RequestTransforms.Count);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Should_LeaveThePermissionsChangedHeaderAlone_When_ACyclopsForwarderTransformsItsResponse(string route)
    {
        using var services = Harness.BuildServices();
        var run = await ApplyAsync(route, services);
        run.HttpContext.Response.Headers[ChangedHeader] = "user-a";

        using var proxyResponse = new HttpResponseMessage();
        proxyResponse.Headers.TryAddWithoutValidation(ChangedHeader, "user-a");
        foreach (var transform in run.BuilderContext.ResponseTransforms)
        {
            await transform.ApplyAsync(new ResponseTransformContext
            {
                HttpContext = run.HttpContext,
                ProxyResponse = proxyResponse,
                HeadersCopied = true,
            });
        }

        Assert.Equal("user-a", run.HttpContext.Response.Headers[ChangedHeader].ToString());
        Assert.Single(run.BuilderContext.ResponseTransforms);
    }
}