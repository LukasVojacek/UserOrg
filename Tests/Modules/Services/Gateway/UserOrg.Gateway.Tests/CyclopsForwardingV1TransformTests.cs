using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using HashiCorpVault.Contracts;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Startup;

using Xunit;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

using Harness = UserOrg.Gateway.Tests.CyclopsForwardingTestHarness;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes <c>/api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}</c> — the v1 route — exactly as
/// it behaved on <c>main</c> before MR !389, which deleted it. The human ruling of 2026-09-12 keeps v1 for
/// backward compatibility, byte for byte, so every assertion here is a <em>pre-MR</em> byte sequence and not a
/// design choice open to revision.
/// <para>
/// What v1 is, and what a well-meaning implementer will be tempted to "fix":
/// <list type="bullet">
/// <item>v1 parses <c>{ResourceName}</c> and never uses it: the query is whole-customer, not per-resource.
/// There is no <c>metadata=LexisResourceName:</c> parameter and the second path segment never leaves the
/// gateway.</item>
/// <item><c>idlist</c> carries a <c>CustomerId</c> obtained from a <c>customerdbAPI</c> lookup, not the
/// project short name the v2 route forwards untranslated.</item>
/// <item>v1 does no date parsing and no range check. <c>fromDate=not-a-date</c> is forwarded verbatim. The
/// <c>TryParseExact</c> tightening T-088-01 made to v2 must not reach v1.</item>
/// <item>v1 has five distinct error bodies where v2 has one shared configuration message, plus a 404 v2 has
/// no equivalent of.</item>
/// <item>The 400 body ends in <c>/...</c>, not in the single dot T-088-01 gave the v2 message.</item>
/// </list>
/// </para>
/// Every case drives the production configurator <c>AddCyclopsUsageV1Transforms</c> through the harness, so
/// the shipped prefix, relative-URL selector and query builder are what is under test.
/// </summary>
public sealed class CyclopsForwardingV1TransformTests
{
    private const string V1RequestPath = $"{Harness.UsageV1Path}{Harness.V1ProjectShortName}/{Harness.V1ResourceName}";

    /// <summary>The whole-customer Cyclops URL v1 builds for the default stubbed lookup.</summary>
    private const string ExpectedV1TargetUri = "https://cyclops.test.local/api/v1/usage?idlist=4711";

    // -------------------------------------------------------------------------------------------------
    // The target URI: TimeUsageRelativeUrl, idlist from the customer lookup, and nothing else.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task V1Route_BuildsTimeUsageUriWithTheCustomerIdInIdlist()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(ExpectedV1TargetUri, run.RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// The ruling turns on this: v1 sends no <c>metadata</c> filter and the resource segment does not reach
    /// Cyclops at all, so a v1 response covers the whole customer rather than one resource.
    /// </summary>
    [Fact]
    public async Task V1Route_SendsNoMetadataParameterAndNoResourceName()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        var query = Harness.RawQuery(run.RequestUri!);
        Assert.Equal(["idlist"], query.Keys.Order().ToArray());
        Assert.DoesNotContain("metadata", run.RequestUri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Harness.V1ResourceName, run.RequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>segments[1]</c> is parsed into a local and never read again: two different resource names under the
    /// same project produce the identical outgoing request.
    /// </summary>
    [Fact]
    public async Task V1Route_ResourceSegmentIsIgnored_DifferentResourcesProduceTheSameTarget()
    {
        using var services = Harness.BuildServices();

        var first = await Harness.ApplyUsageV1Async(
            Harness.BuildHttpContext($"{Harness.UsageV1Path}{Harness.V1ProjectShortName}/resource-one", services));
        var second = await Harness.ApplyUsageV1Async(
            Harness.BuildHttpContext($"{Harness.UsageV1Path}{Harness.V1ProjectShortName}/resource-two", services));

        Assert.Equal(ExpectedV1TargetUri, first.RequestUri!.AbsoluteUri);
        Assert.Equal(first.RequestUri.AbsoluteUri, second.RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// v1 and v2 hit the same Cyclops endpoint — that is what makes v1's reuse of
    /// <see cref="CyclopsOptions.TimeUsageRelativeUrl"/> safe and a third configuration key unnecessary — and
    /// differ only in the query they build.
    /// </summary>
    [Fact]
    public async Task V1AndV2ResolveTheSameCyclopsEndpointAndDifferOnlyInTheQuery()
    {
        using var services = Harness.BuildServices();

        var v1 = await Harness.ApplyUsageV1Async(Harness.BuildHttpContext(V1RequestPath, services));
        var v2 = await Harness.ApplyUsageAsync(Harness.BuildHttpContext(
            $"{Harness.UsagePath}{Harness.V1ProjectShortName}/{Harness.V1ResourceName}", services));

        Assert.Equal(
            v2.RequestUri!.GetLeftPart(UriPartial.Path),
            v1.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.NotEqual(v2.RequestUri.Query, v1.RequestUri.Query);

        var v1Query = Harness.RawQuery(v1.RequestUri);
        var v2Query = Harness.RawQuery(v2.RequestUri);
        Assert.Equal(Harness.V1CustomerId, v1Query["idlist"]);
        Assert.Equal(Harness.V1ProjectShortName, v2Query["idlist"]);
        Assert.DoesNotContain("metadata", v1Query.Keys);
        Assert.Contains("metadata", v2Query.Keys);
    }

    // The customerdbAPI lookup.

    [Fact]
    public async Task V1Route_LooksTheCustomerUpOnTheNamedClientCarryingTheVaultApiKey()
    {
        const string headerName = "X-Cyclops-Token";
        var vault = Harness.VaultReturning(Harness.Credentials(headerName: headerName));
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson);
        using var services = Harness.BuildServices(vault.Object, customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        var call = Assert.Single(customerApi.Calls);
        Assert.Equal(Harness.V1CustomerClientName, call.ClientName);
        Assert.Equal($"{Harness.V1CustomerApiUrlPrefix}{Harness.V1ProjectShortName}", call.RequestUri!.AbsoluteUri);
        Assert.Equal(Harness.VaultApiKey, call.HeaderValue(headerName));
        Assert.Equal(ExpectedV1TargetUri, run.RequestUri!.AbsoluteUri);
    }

    /// <summary>The search term is escaped: a project short name with a space must not break the query.</summary>
    [Fact]
    public async Task V1Route_EscapesTheProjectShortNameInTheCustomerSearch()
    {
        const string projectShortName = "proj a";
        var customerApi = StubCustomerApi.Returning(Harness.CustomerJson((projectShortName, Harness.V1CustomerId)));
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(
            $"{Harness.UsageV1Path}{projectShortName}/{Harness.V1ResourceName}", services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        var call = Assert.Single(customerApi.Calls);
        Assert.Equal($"{Harness.V1CustomerApiUrlPrefix}proj%20a", call.RequestUri!.AbsoluteUri);
        Assert.Equal(Harness.V1CustomerId, Harness.RawQuery(run.RequestUri!)["idlist"]);
    }

    /// <summary>The <c>Name</c> match is case-insensitive: <c>PROJ-1</c> answers a request for <c>proj-1</c>.</summary>
    [Fact]
    public async Task V1Route_MatchesTheCustomerNameCaseInsensitively()
    {
        var customerApi = StubCustomerApi.Returning(Harness.CustomerJson(("PROJ-1", Harness.V1CustomerId)));
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(200, run.StatusCode);
        Assert.Equal(ExpectedV1TargetUri, run.RequestUri!.AbsoluteUri);
    }

    /// <summary>The matching customer is picked out of the page, not the first one returned.</summary>
    [Fact]
    public async Task V1Route_PicksTheMatchingCustomerNotTheFirstOneReturned()
    {
        var customerApi = StubCustomerApi.Returning(Harness.CustomerJson(
            ("proj-1-staging", "9999"),
            (Harness.V1ProjectShortName, Harness.V1CustomerId)));
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(Harness.V1CustomerId, Harness.RawQuery(run.RequestUri!)["idlist"]);
    }

    // -------------------------------------------------------------------------------------------------
    // Date bounds: forwarded verbatim, unparsed and unvalidated. This is the v1/v2 divergence most likely
    // to be "fixed" by accident — T-088-01 has just tightened the v2 parse to ISO-8601 TryParseExact.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task V1Route_BothDateBounds_AreForwardedAsFromAndTo()
    {
        const string from = "2024-01-01T00:00:00Z";
        const string to = "2024-02-01T00:00:00Z";
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services, fromDate: from, toDate: to);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        var query = Harness.RawQuery(run.RequestUri!);
        Assert.Equal(from, Uri.UnescapeDataString(query["from"]));
        Assert.Equal(to, Uri.UnescapeDataString(query["to"]));
        Assert.Equal(Harness.V1CustomerId, query["idlist"]);
    }

    [Fact]
    public async Task V1Route_NoDateBounds_OmitsFromAndToEntirely()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        var query = Harness.RawQuery(run.RequestUri!);
        Assert.DoesNotContain("from", query.Keys);
        Assert.DoesNotContain("to", query.Keys);
    }

    [Fact]
    public async Task V1Route_OnlyToDate_ForwardsToAndOmitsFrom()
    {
        const string to = "2024-02-01T00:00:00Z";
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services, toDate: to);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        var query = Harness.RawQuery(run.RequestUri!);
        Assert.Equal(to, Uri.UnescapeDataString(query["to"]));
        Assert.DoesNotContain("from", query.Keys);
    }

    /// <summary>
    /// <c>fromDate=not-a-date</c> is forwarded as <c>from=not-a-date</c>. v1 parses no dates at all, so it
    /// cannot reject one; adding the v2 400 here would break every client that relies on Cyclops' own
    /// interpretation of the bound.
    /// </summary>
    [Theory]
    [InlineData("not-a-date")]
    [InlineData("12/31/2024")]
    [InlineData("yesterday")]
    public async Task V1Route_UnparseableDateBound_IsForwardedVerbatimAndNotRejected(string from)
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services, fromDate: from);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(200, run.StatusCode);
        Assert.Empty(run.ResponseBody);
        Assert.NotNull(run.RequestUri);
        Assert.Equal(from, Uri.UnescapeDataString(Harness.RawQuery(run.RequestUri)["from"]));
    }

    /// <summary>
    /// An inverted range is forwarded too: v1 has no <c>toDate must be after fromDate</c> check, and
    /// introducing one would be a compatibility break.
    /// </summary>
    [Fact]
    public async Task V1Route_InvertedDateRange_IsForwardedWithoutARangeCheck()
    {
        const string from = "2024-02-01T00:00:00Z";
        const string to = "2024-01-01T00:00:00Z";
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services, fromDate: from, toDate: to);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(200, run.StatusCode);
        var query = Harness.RawQuery(run.RequestUri!);
        Assert.Equal(from, Uri.UnescapeDataString(query["from"]));
        Assert.Equal(to, Uri.UnescapeDataString(query["to"]));
    }

    /// <summary>
    /// v1 tests presence with <c>Query.TryGetValue</c>, not emptiness: <c>?fromDate=</c> is present, so
    /// <c>from=</c> is forwarded empty. v2 treats the same input as absent — the two must not be aligned.
    /// </summary>
    [Fact]
    public async Task V1Route_EmptyDateBound_IsForwardedAsAnEmptyParameter()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services, fromDate: string.Empty);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(200, run.StatusCode);
        var query = Harness.RawQuery(run.RequestUri!);
        Assert.Contains("from", query.Keys);
        Assert.Empty(query["from"]);
    }

    // -------------------------------------------------------------------------------------------------
    // Path handling: two segments required, surplus ones ignored (v2 rejects them since F7).
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task V1Route_SurplusPathSegments_AreIgnoredAndTheRequestIsForwarded()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext($"{V1RequestPath}/extra/segments", services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(200, run.StatusCode);
        Assert.Equal(ExpectedV1TargetUri, run.RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// Fewer than two segments is the v1 400, whose body ends in <c>/...</c> — T-088-01 shortened the v2
    /// message's tail to a single dot and that change must not be carried over here.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("proj-1")]
    [InlineData("proj-1/")]
    public async Task V1Route_FewerThanTwoSegments_Returns400WithTheV1Body(string remainder)
    {
        var vault = Harness.VaultReturning(Harness.Credentials());
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson);
        using var services = Harness.BuildServices(vault.Object, customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext($"{Harness.UsageV1Path}{remainder}", services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal(Harness.V1ExpectedPathText, run.ResponseBody);
        Assert.EndsWith("/...", run.ResponseBody, StringComparison.Ordinal);
        Assert.Null(run.RequestUri);
        Assert.Empty(customerApi.Calls);
        vault.Verify(v => v.GetExternalCredentials(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------------------------------
    // The five error bodies. Each one is distinct, and none of them is v2's shared configuration message.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task V1Route_VaultLookupFails_Returns500ServerSecretsConfigurationError()
    {
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson);
        using var services = Harness.BuildServices(Harness.VaultFailing("vault is sealed").Object, customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(Harness.V1VaultFailureErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
        Assert.Empty(customerApi.Calls);
        Assert.DoesNotContain(Harness.VaultApiKey, run.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V1Route_EmptyApiKey_Returns500ServerConfigurationError()
    {
        var vault = Harness.VaultReturning(Harness.Credentials(apiKey: string.Empty));
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson);
        using var services = Harness.BuildServices(vault.Object, customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(Harness.V1ApiKeyMissingErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
        Assert.Empty(customerApi.Calls);
    }

    /// <summary>
    /// The two configuration failures answer with different bodies, and neither is the single
    /// <c>Server is not correctly configured.</c> the v2 route collapsed them into. Collapsing them here
    /// would itself be the compatibility break this block exists to prevent.
    /// </summary>
    [Fact]
    public async Task V1Route_ConfigurationErrorBodiesAreDistinctAndAreNotTheV2SharedMessage()
    {
        using var vaultFailureServices = Harness.BuildServices(Harness.VaultFailing("vault is sealed").Object);
        using var emptyKeyServices = Harness.BuildServices(
            Harness.VaultReturning(Harness.Credentials(apiKey: string.Empty)).Object);

        var vaultFailure = await Harness.ApplyUsageV1Async(
            Harness.BuildHttpContext(V1RequestPath, vaultFailureServices));
        var emptyKey = await Harness.ApplyUsageV1Async(
            Harness.BuildHttpContext(V1RequestPath, emptyKeyServices));

        Assert.NotEqual(vaultFailure.ResponseBody, emptyKey.ResponseBody);
        Assert.NotEqual(Harness.ConfigurationErrorText, vaultFailure.ResponseBody);
        Assert.NotEqual(Harness.ConfigurationErrorText, emptyKey.ResponseBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task V1Route_CustomerApiNonSuccess_Returns500FailedToRetrieveCustomerInformation(HttpStatusCode status)
    {
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson, status);
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(Harness.V1CustomerLookupErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
        Assert.DoesNotContain(Harness.VaultApiKey, run.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V1Route_CustomerApiThrows_Returns500ErrorCommunicatingWithCyclops()
    {
        var customerApi = StubCustomerApi.Throwing(new HttpRequestException("connection refused"));
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(Harness.V1CommunicationErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"customers\":")]
    [InlineData("{\"customers\":{}}")]
    public async Task V1Route_MalformedCustomerJson_Returns500ErrorProcessingCyclopsResponse(string body)
    {
        var customerApi = StubCustomerApi.Returning(body);
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(500, run.StatusCode);
        Assert.Equal(Harness.V1ResponseProcessingErrorText, run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    /// <summary>
    /// The five v1 failure modes answer with five different bodies. Driven end to end, one run per mode:
    /// asserting distinctness over the harness constants instead would stay green even if production
    /// collapsed all five into one message, which is exactly the compatibility break this is here to pin.
    /// </summary>
    [Fact]
    public async Task V1ErrorBodiesAreFiveDistinctTexts()
    {
        var bodies = new List<string>();

        foreach (var (vault, customerApi) in FiveFailureModes())
        {
            using var services = Harness.BuildServices(vault, customerApi: customerApi);
            var run = await Harness.ApplyUsageV1Async(Harness.BuildHttpContext(V1RequestPath, services));

            Assert.Equal(500, run.StatusCode);
            Assert.NotNull(run.ResponseBody);
            bodies.Add(run.ResponseBody);
        }

        Assert.Equal(5, bodies.Count);
        Assert.Equal(5, bodies.Distinct(StringComparer.Ordinal).Count());
    }

    private static IEnumerable<(IHashiCorpVaultClient? Vault, StubCustomerApi CustomerApi)> FiveFailureModes()
    {
        yield return (Harness.VaultFailing("vault is sealed").Object, StubCustomerApi.Returning(Harness.DefaultCustomerJson));
        yield return (Harness.VaultReturning(Harness.Credentials(apiKey: string.Empty)).Object, StubCustomerApi.Returning(Harness.DefaultCustomerJson));
        yield return (null, StubCustomerApi.Returning(Harness.DefaultCustomerJson, HttpStatusCode.InternalServerError));
        yield return (null, StubCustomerApi.Throwing(new HttpRequestException("connection refused")));
        yield return (null, StubCustomerApi.Returning("not json at all"));
    }

    // -------------------------------------------------------------------------------------------------
    // The 404 — v2 has no equivalent of it.
    // -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{\"customers\":[]}")]
    [InlineData("{}")]
    [InlineData("{\"customers\":null}")]
    public async Task V1Route_NoCustomersReturned_Returns404CustomerNotFound(string body)
    {
        var customerApi = StubCustomerApi.Returning(body);
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(404, run.StatusCode);
        Assert.Equal(Harness.V1CustomerNotFoundText(Harness.V1ProjectShortName), run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    [Fact]
    public async Task V1Route_NoCustomerNameMatches_Returns404CustomerNotFound()
    {
        var customerApi = StubCustomerApi.Returning(Harness.CustomerJson(
            ("some-other-project", "1"),
            ("proj-1-staging", "2")));
        using var services = Harness.BuildServices(customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(404, run.StatusCode);
        Assert.Equal(Harness.V1CustomerNotFoundText(Harness.V1ProjectShortName), run.ResponseBody);
        Assert.Null(run.RequestUri);
        Assert.DoesNotContain(Harness.VaultApiKey, run.ResponseBody, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------
    // The API key, the permission check and the short-circuit guard.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task V1Route_ApiKeyIsSetOnProxyRequestOnceUnderTheVaultHeaderName()
    {
        const string headerName = "X-Cyclops-Token";
        var vault = Harness.VaultReturning(Harness.Credentials(headerName: headerName));
        using var services = Harness.BuildServices(vault.Object);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(Harness.VaultApiKey, Assert.Single(run.ProxyRequest.Headers.GetValues(headerName)));
        Assert.False(run.ProxyRequest.Headers.Contains(Harness.VaultHeaderName));
        Assert.DoesNotContain(headerName, run.HttpContext.Response.Headers.Keys);
    }

    [Fact]
    public async Task V1ResponseTransform_RemovesTheVaultHeaderFromTheResponse()
    {
        const string headerName = "X-Cyclops-Token";
        var vault = Harness.VaultReturning(Harness.Credentials(headerName: headerName));
        using var services = Harness.BuildServices(vault.Object);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var builderContext = new TransformBuilderContext();
        builderContext.AddCyclopsUsageV1Transforms();
        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };

        // The request transform is what learns the header name from the vault; the response transform strips
        // whatever that run recorded.
        await builderContext.RequestTransforms[^1].ApplyAsync(requestContext);

        httpContext.Response.Headers[headerName] = Harness.VaultApiKey;
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
    /// The v1 route registers the permission check first, exactly as the two v2 routes do: a caller without
    /// the endpoint permission is rejected by transform 0 before transform 1 builds anything.
    /// </summary>
    [Fact]
    public async Task V1Route_PermissionCheckTransformIsRegisteredBeforeTheCyclopsTransform()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var builderContext = new TransformBuilderContext();
        builderContext.AddCyclopsUsageV1Transforms();

        Assert.Equal(2, builderContext.RequestTransforms.Count);

        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };

        await builderContext.RequestTransforms[0].ApplyAsync(requestContext);
        Assert.Equal(401, httpContext.Response.StatusCode);
        Assert.Null(requestContext.ProxyRequest.RequestUri);
    }

    [Fact]
    public async Task V1Route_ResponseAlreadyStarted_LeavesProxyRequestUntouched()
    {
        var vault = Harness.VaultReturning(Harness.Credentials());
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson);
        using var services = Harness.BuildServices(vault.Object, customerApi: customerApi);
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services, responseHasStarted: true);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Null(run.RequestUri);
        Assert.Empty(run.ProxyRequest.Headers);
        Assert.Empty(run.ResponseBody);
        Assert.Empty(customerApi.Calls);
        vault.Verify(v => v.GetExternalCredentials(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The three routes coexist and stay separate.

    /// <summary>
    /// All three forwarders run against the same container: each builds its own target from its own prefix,
    /// and the <c>customerdbAPI</c> lookup happens on the v1 route only.
    /// </summary>
    [Fact]
    public async Task ThreeRoutesCoexist_AndOnlyV1CallsTheCustomerApi()
    {
        var customerApi = StubCustomerApi.Returning(Harness.DefaultCustomerJson);
        using var services = Harness.BuildServices(customerApi: customerApi);

        var v1 = await Harness.ApplyUsageV1Async(Harness.BuildHttpContext(V1RequestPath, services));
        Assert.Single(customerApi.Calls);

        var v2Usage = await Harness.ApplyUsageAsync(Harness.BuildHttpContext(
            $"{Harness.UsagePath}{Harness.V1ProjectShortName}/{Harness.V1ResourceName}", services));
        var v2TotalUsage = await Harness.ApplyTotalUsageAsync(Harness.BuildHttpContext(
            $"{Harness.TotalUsagePath}{Harness.V1ResourceName}", services));

        Assert.Equal(ExpectedV1TargetUri, v1.RequestUri!.AbsoluteUri);
        Assert.Equal(
            "https://cyclops.test.local/api/v1/usage?idlist=proj-1&metadata=LexisResourceName:res-b",
            v2Usage.RequestUri!.AbsoluteUri);
        Assert.Equal(
            "https://cyclops.test.local/api/v1/totalusage?metadata=LexisResourceName:res-b",
            v2TotalUsage.RequestUri!.AbsoluteUri);

        // Still exactly the one call the v1 run made: neither v2 route translates a project short name.
        Assert.Single(customerApi.Calls);
    }

    /// <summary>The v1 transform owns its prefix only: a v2 path handed to it is a malformed v1 path.</summary>
    [Fact]
    public async Task V1Transform_GivenAV2Path_Returns400()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(
            $"{Harness.UsagePath}{Harness.V1ProjectShortName}/{Harness.V1ResourceName}", services);

        var run = await Harness.ApplyUsageV1Async(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.Equal(Harness.V1ExpectedPathText, run.ResponseBody);
        Assert.Null(run.RequestUri);
    }

    /// <summary>
    /// The mirror image, green today: the v2 transform does not answer the v1 path, so restoring v1 cannot
    /// be done by pointing the old prefix at the v2 transform.
    /// </summary>
    [Fact]
    public async Task V2Transform_GivenTheV1Path_Returns400()
    {
        using var services = Harness.BuildServices();
        var httpContext = Harness.BuildHttpContext(V1RequestPath, services);

        var run = await Harness.ApplyUsageAsync(httpContext);

        Assert.Equal(400, run.StatusCode);
        Assert.StartsWith(Harness.UsageExpectedPathText, run.ResponseBody, StringComparison.Ordinal);
        Assert.Null(run.RequestUri);
    }
}
