using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using HashiCorpVault.Contracts;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Startup;

using Xunit;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the Cyclops forwarder as MR !389 shipped it: the target URI both routes build, the optional
/// date bounds, the 400s, the two 500s, the vault API-key injection and the response-header strip. Every case
/// drives the production configurator for the route it names
/// (<see cref="CyclopsForwardingExtensions.AddCyclopsUsageTransforms"/> /
/// <see cref="CyclopsForwardingExtensions.AddCyclopsTotalUsageTransforms"/>), so the shipped prefix,
/// relative-URL selector and query builder are what is under test.
/// </summary>
public sealed class CyclopsForwardingTransformTests
{
    [Fact]
    public async Task UsageRoute_BuildsTimeUsageUriWithIdlistAndMetadata()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(
            "https://cyclops.test.local/api/v1/usage?idlist=proj-a&metadata=LexisResourceName:res-b",
            run.RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// A provider-qualified resource key carries a <c>|</c>, which must reach Cyclops percent-encoded:
    /// the value is <see cref="Uri.EscapeDataString(string)"/>d before it goes into the query. The request
    /// path is the decoded form ASP.NET Core hands the pipeline, hence the literal <c>|</c> here.
    /// </summary>
    [Fact]
    public async Task UsageRoute_ProviderQualifiedResourceName_EscapesPipe()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/acme|hpc-1", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        var query = CyclopsForwardingTestHarness.RawQuery(run.RequestUri!);
        Assert.Equal("LexisResourceName:acme%7Chpc-1", query["metadata"]);
        Assert.DoesNotContain("|", run.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contract change MR !389 makes: the <c>customerdbAPI</c> lookup that used to translate
    /// <c>ProjectShortName</c> → <c>CustomerId</c> is gone, and <c>idlist</c> now carries the project short
    /// name verbatim. The vault is the only outbound call the transform makes.
    /// </summary>
    [Fact]
    public async Task UsageRoute_IdlistCarriesProjectShortNameVerbatim_WithNoCustomerIdTranslation()
    {
        var vault = CyclopsForwardingTestHarness.VaultReturning(CyclopsForwardingTestHarness.Credentials());
        using var services = CyclopsForwardingTestHarness.BuildServices(vault.Object);
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        var query = CyclopsForwardingTestHarness.RawQuery(run.RequestUri!);
        Assert.Equal("proj-a", query["idlist"]);
        Assert.DoesNotContain("customer", run.RequestUri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

        vault.Verify(
            v => v.GetExternalCredentials(CyclopsForwardingTestHarness.VaultCredentialName, It.IsAny<CancellationToken>()),
            Times.Once);
        vault.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TotalUsageRoute_BuildsTotalUsageUriWithMetadataOnly()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.TotalUsagePath}res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyTotalUsageAsync(httpContext);

        Assert.Equal(
            "https://cyclops.test.local/api/v1/totalusage?metadata=LexisResourceName:res-b",
            run.RequestUri!.AbsoluteUri);
        Assert.DoesNotContain("idlist", CyclopsForwardingTestHarness.RawQuery(run.RequestUri).Keys);
    }

    [Fact]
    public async Task UsageRoute_BothDateBounds_AreForwardedVerbatimAsFromAndTo()
    {
        const string from = "2024-01-01T00:00:00Z";
        const string to = "2024-02-01T00:00:00Z";
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: from, toDate: to);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        var query = CyclopsForwardingTestHarness.RawQuery(run.RequestUri!);
        Assert.Equal(from, Uri.UnescapeDataString(query["from"]));
        Assert.Equal(to, Uri.UnescapeDataString(query["to"]));
    }

    [Fact]
    public async Task UsageRoute_NoDateBounds_OmitsFromAndToEntirely()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        var query = CyclopsForwardingTestHarness.RawQuery(run.RequestUri!);
        Assert.DoesNotContain("from", query.Keys);
        Assert.DoesNotContain("to", query.Keys);
        Assert.Equal(["idlist", "metadata"], query.Keys.Order().ToArray());
    }

    /// <summary>
    /// An empty <c>fromDate=</c> is treated as absent, not as a malformed bound: the parameter does not reach
    /// Cyclops at all, and the request is not rejected.
    /// </summary>
    [Fact]
    public async Task UsageRoute_EmptyDateBound_IsTreatedAsAbsent()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: string.Empty);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(200, run.StatusCode);
        Assert.DoesNotContain("from", CyclopsForwardingTestHarness.RawQuery(run.RequestUri!).Keys);
    }

    [Fact]
    public async Task UsageRoute_OnlyFromDate_ForwardsFromAndOmitsTo()
    {
        const string from = "2024-01-01T00:00:00Z";
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: from);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        var query = CyclopsForwardingTestHarness.RawQuery(run.RequestUri!);
        Assert.Equal(from, Uri.UnescapeDataString(query["from"]));
        Assert.DoesNotContain("to", query.Keys);
    }

    /// <summary>
    /// The ISO-8601 shapes the forwarder accepts today and must keep accepting — the date-format defect (F6)
    /// is about the shapes that are <em>not</em> ISO-8601, not about these.
    /// </summary>
    [Theory]
    [InlineData("2024-01-01")]
    [InlineData("2024-01-01T12:30:00Z")]
    [InlineData("2024-01-01T12:30:00+02:00")]
    [InlineData("2024-01-01T12:30:00.1234567Z")]
    public async Task UsageRoute_IsoDateBound_IsAcceptedAndForwarded(string from)
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: from);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(200, run.StatusCode);
        Assert.Equal(from, Uri.UnescapeDataString(CyclopsForwardingTestHarness.RawQuery(run.RequestUri!)["from"]));
    }

    [Fact]
    public async Task UsageRoute_TooFewSegments_Returns400NamingTheExpectedPath()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.StartsWith(CyclopsForwardingTestHarness.UsageExpectedPathText, run.ResponseBody, StringComparison.Ordinal);
        Assert.Null(run.RequestUri);
    }

    [Fact]
    public async Task TotalUsageRoute_NoSegments_Returns400NamingTheExpectedPath()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            CyclopsForwardingTestHarness.TotalUsagePath, services);

        var run = await CyclopsForwardingTestHarness.ApplyTotalUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.StartsWith(CyclopsForwardingTestHarness.TotalUsageExpectedPathText, run.ResponseBody, StringComparison.Ordinal);
        Assert.Null(run.RequestUri);
    }

    [Fact]
    public async Task UsageRoute_MalformedFromDate_Returns400()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: "not-a-date");

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal(CyclopsForwardingTestHarness.InvalidDateErrorText("fromDate"), run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    [Fact]
    public async Task UsageRoute_MalformedToDate_Returns400()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b",
            services,
            fromDate: "2024-01-01T00:00:00Z",
            toDate: "not-a-date");

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal(CyclopsForwardingTestHarness.InvalidDateErrorText("toDate"), run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00Z", "2024-01-01T00:00:00Z")]
    [InlineData("2024-02-01T00:00:00Z", "2024-01-01T00:00:00Z")]
    public async Task UsageRoute_ToDateNotAfterFromDate_Returns400(string from, string to)
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, fromDate: from, toDate: to);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal(CyclopsForwardingTestHarness.InvalidDateRangeErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    [Fact]
    public async Task UsageRoute_VaultLookupFails_Returns500AndLeaksNothing()
    {
        var vault = CyclopsForwardingTestHarness.VaultFailing("vault is sealed");
        using var services = CyclopsForwardingTestHarness.BuildServices(vault.Object);
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(CyclopsForwardingTestHarness.ConfigurationErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
        Assert.False(run.ProxyRequest.Headers.Contains(CyclopsForwardingTestHarness.VaultHeaderName));
        Assert.DoesNotContain(CyclopsForwardingTestHarness.VaultApiKey, run.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsageRoute_VaultReturnsEmptyApiKey_Returns500AndLeaksNothing()
    {
        var vault = CyclopsForwardingTestHarness.VaultReturning(CyclopsForwardingTestHarness.Credentials(apiKey: string.Empty));
        using var services = CyclopsForwardingTestHarness.BuildServices(vault.Object);
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(CyclopsForwardingTestHarness.ConfigurationErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
        Assert.False(run.ProxyRequest.Headers.Contains(CyclopsForwardingTestHarness.VaultHeaderName));
        Assert.DoesNotContain(CyclopsForwardingTestHarness.VaultHeaderName, run.HttpContext.Response.Headers.Keys);
    }

    /// <summary>
    /// The key goes on the proxied request only, under the header name the vault returned, once — and never
    /// onto the response the caller sees.
    /// </summary>
    [Fact]
    public async Task UsageRoute_ApiKeyIsSetOnProxyRequestOnceUnderTheVaultHeaderName()
    {
        const string headerName = "X-Cyclops-Token";
        var vault = CyclopsForwardingTestHarness.VaultReturning(
            CyclopsForwardingTestHarness.Credentials(headerName: headerName));
        using var services = CyclopsForwardingTestHarness.BuildServices(vault.Object);
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Equal(CyclopsForwardingTestHarness.VaultApiKey, Assert.Single(run.ProxyRequest.Headers.GetValues(headerName)));
        Assert.False(run.ProxyRequest.Headers.Contains(CyclopsForwardingTestHarness.VaultHeaderName));
        Assert.DoesNotContain(headerName, run.HttpContext.Response.Headers.Keys);
    }

    /// <summary>
    /// The response transform strips the vault header name from the response, so an upstream echo of the key
    /// cannot reach the caller. One response transform is registered.
    /// </summary>
    [Fact]
    public async Task ResponseTransform_RemovesTheVaultHeaderFromTheResponse()
    {
        const string headerName = "X-Cyclops-Token";
        var vault = CyclopsForwardingTestHarness.VaultReturning(
            CyclopsForwardingTestHarness.Credentials(headerName: headerName));
        using var services = CyclopsForwardingTestHarness.BuildServices(vault.Object);
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var builderContext = new TransformBuilderContext();
        builderContext.AddCyclopsUsageTransforms();
        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };

        // The request transform is what learns the header name from the vault; the response transform
        // strips whatever that run recorded.
        await builderContext.RequestTransforms[^1].ApplyAsync(requestContext);

        httpContext.Response.Headers[headerName] = CyclopsForwardingTestHarness.VaultApiKey;
        var responseTransform = Assert.Single(builderContext.ResponseTransforms);
        using var proxyResponse = new HttpResponseMessage();
        await responseTransform.ApplyAsync(new ResponseTransformContext
        {
            HttpContext = httpContext,
            ProxyResponse = proxyResponse,
            HeadersCopied = true
        });

        Assert.DoesNotContain(headerName, httpContext.Response.Headers.Keys);
    }

    /// <summary>
    /// The route registers two request transforms and the permission check is the first of them: an
    /// unauthenticated caller is rejected by transform 0 before transform 1 ever builds a Cyclops URI.
    /// </summary>
    [Fact]
    public async Task PermissionCheckTransformIsRegisteredBeforeTheCyclopsTransform()
    {
        using var services = CyclopsForwardingTestHarness.BuildServices();
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services);

        var builderContext = new TransformBuilderContext();
        builderContext.AddCyclopsUsageTransforms();

        Assert.Equal(2, builderContext.RequestTransforms.Count);

        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };

        // Transform 0 is the permission check: an unauthenticated principal gets 401 and no Cyclops URI.
        await builderContext.RequestTransforms[0].ApplyAsync(requestContext);
        Assert.Equal(401, httpContext.Response.StatusCode);
        Assert.Null(requestContext.ProxyRequest.RequestUri);

        // Transform 1 is the Cyclops one: on a fresh context it is what sets the Cyclops URI.
        var cyclopsRun = await CyclopsForwardingTestHarness.ApplyUsageAsync(
            CyclopsForwardingTestHarness.BuildHttpContext($"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services));
        Assert.StartsWith("https://cyclops.test.local/api/v1/usage", cyclopsRun.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// When the permission check has already written the response, the Cyclops transform must leave the
    /// proxied request alone — and must not spend a vault lookup on it.
    /// </summary>
    [Fact]
    public async Task CyclopsTransform_ResponseAlreadyStarted_LeavesProxyRequestUntouched()
    {
        var vault = CyclopsForwardingTestHarness.VaultReturning(CyclopsForwardingTestHarness.Credentials());
        using var services = CyclopsForwardingTestHarness.BuildServices(vault.Object);
        var httpContext = CyclopsForwardingTestHarness.BuildHttpContext(
            $"{CyclopsForwardingTestHarness.UsagePath}proj-a/res-b", services, responseHasStarted: true);

        var run = await CyclopsForwardingTestHarness.ApplyUsageAsync(httpContext);

        Assert.Null(run.RequestUri);
        Assert.Empty(run.ProxyRequest.Headers);
        Assert.Empty(run.ResponseBody);
        vault.Verify(
            v => v.GetExternalCredentials(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
