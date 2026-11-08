using System;
using System.Threading.Tasks;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The three behaviour changes T-088 makes to the Cyclops forwarder. These are <em>red</em> against the
/// forwarder as MR !389 shipped it and turn green with the fixes:
/// <list type="bullet">
/// <item>F5 — the malformed-path 400 body ends in two dots.</item>
/// <item>F6 — the body says "ISO-8601" while <c>DateTimeOffset.TryParse</c> with the invariant culture also
/// accepts <c>12/31/2024</c>. Pinned on the <em>parse</em> side: the message is the shipped contract, and a
/// slash date is locale-ambiguous, so the parse tightens to ISO-8601 forms and the wording stays.</item>
/// <item>F7 — path segments past the route's required count are silently dropped instead of rejected.</item>
/// </list>
/// The shapes that must keep parsing are pinned green by
/// <c>CyclopsForwardingTransformTests.UsageRoute_IsoDateBound_IsAcceptedAndForwarded</c>.
/// </summary>
public sealed class CyclopsForwardingDefectTests
{
    /// <summary>F5: the 400 body ends with one dot, not two.</summary>
    [Fact]
    public async Task UsageRoute_TooFewSegments_BodyEndsWithASingleDot()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal($"{CyclopsForwardingTestHarness.UsageExpectedPathText}.", run.ResponseBody);
    }

    /// <summary>
    /// F6: <c>12/31/2024</c> is not ISO-8601 and is locale-ambiguous (31 January vs 12 December to a European
    /// caller), so it must be rejected with the message the forwarder already advertises.
    /// </summary>
    [Theory]
    [InlineData("fromDate", "12/31/2024", null)]
    [InlineData("toDate", "2024-01-01T00:00:00Z", "12/31/2024")]
    public async Task UsageRoute_SlashSeparatedDate_IsRejectedAsNotIso8601(string parameterName, string from, string? to)
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: from, toDate: to);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal(CyclopsForwardingTestHarness.InvalidDateErrorText(parameterName), run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    /// <summary>
    /// F7: the route pattern is <c>{pathPrefix}{{**catch-all}}</c>, so a caller can append any number of extra
    /// segments and today they are dropped. Reject them with the same 400 as a malformed path.
    /// </summary>
    [Fact]
    public async Task TotalUsageRoute_SurplusPathSegments_Returns400()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.TotalUsagePath}res-b/surplus/segments", services);

        var run = await CyclopsForwardingTestHarness.ApplyTotalUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.StartsWith(CyclopsForwardingTestHarness.TotalUsageExpectedPathText, run.ResponseBody, StringComparison.Ordinal);
        Assert.Null(run.RequestUri);
    }

    /// <summary>F7 on the two-segment route as well.</summary>
    [Fact]
    public async Task UsageRoute_SurplusPathSegments_Returns400()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b/surplus", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.StartsWith(CyclopsForwardingTestHarness.UsageExpectedPathText, run.ResponseBody, StringComparison.Ordinal);
        Assert.Null(run.RequestUri);
    }
}
