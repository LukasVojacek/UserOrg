using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Drives the Cyclops forwarder the way <see cref="InternalTokenTransformTestHarness"/> drives the internal
/// token transform: a plain <see cref="TransformBuilderContext"/>, the <em>production</em> configurator applied
/// to it (<see cref="CyclopsForwardingExtensions.AddCyclopsUsageTransforms"/> /
/// <see cref="CyclopsForwardingExtensions.AddCyclopsTotalUsageTransforms"/>, which carry the shipped prefix,
/// relative-URL selector and query builder), then <c>ApplyAsync</c> over a <see cref="DefaultHttpContext"/>.
/// No test re-types <c>idlist=</c> or <c>metadata=LexisResourceName:</c>; they come from the shipped code.
/// </summary>
internal static class CyclopsForwardingTestHarness
{
    internal const string VaultCredentialName = "cyclops";
    internal const string VaultHeaderName = "X-Api-Key";
    internal const string VaultApiKey = "cyclops-api-key-value";

    internal static readonly Uri BaseUrl = new("https://cyclops.test.local/");
    internal static readonly Uri TimeUsageRelativeUrl = new("/api/v1/usage", UriKind.Relative);
    internal static readonly Uri TotalUsageRelativeUrl = new("/api/v1/totalusage", UriKind.Relative);

    internal const string UsagePath = "/api/Cyclops/Usage/";
    internal const string TotalUsagePath = "/api/Cyclops/TotalUsage/";

    /// <summary>The 400 body prefix the usage route reports, minus the trailing dot(s) — see F5.</summary>
    internal const string UsageExpectedPathText = "Bad Request: expected /api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}";

    internal const string TotalUsageExpectedPathText = "Bad Request: expected /api/Cyclops/TotalUsage/{ProviderResourceName}";

    internal const string ConfigurationErrorText = "Server is not correctly configured.";
    internal const string InvalidDateRangeErrorText = "Bad Request: toDate must be after fromDate.";

    internal static string InvalidDateErrorText(string parameterName)
        => $"Bad Request: {parameterName} must be a valid ISO-8601 date.";

    // ---------------------------------------------------------------------------------------------------
    // v1 — /api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}, kept for backward compatibility.
    // Every literal below is the pre-MR !389 byte sequence; none of it may be aligned onto its v2 sibling.
    // ---------------------------------------------------------------------------------------------------

    internal const string UsageV1Path = "/api/Other/CyclopsUsage/";

    /// <summary>The v1 400 body, verbatim — it ends in <c>/...</c>, not in the v2 route's single dot.</summary>
    internal const string V1ExpectedPathText = "Bad Request: expected /api/Other/CyclopsUsage/{project}/{resource}/...";

    /// <summary>Vault lookup failed. Distinct from <see cref="V1ApiKeyMissingErrorText"/> — v1 has five texts.</summary>
    internal const string V1VaultFailureErrorText = "Server secrets configuration error";

    internal const string V1ApiKeyMissingErrorText = "Server configuration error";
    internal const string V1CustomerLookupErrorText = "Failed to retrieve customer information from Cyclops";
    internal const string V1CommunicationErrorText = "Error communicating with Cyclops service";
    internal const string V1ResponseProcessingErrorText = "Error processing Cyclops response";

    internal static string V1CustomerNotFoundText(string projectShortName)
        => $"Customer not found for project: {projectShortName}";

    /// <summary>The named client registered in <c>Program.cs</c>; the customer lookup must use this name.</summary>
    internal const string V1CustomerClientName = "cyclops-customer";

    internal const string V1ProjectShortName = "proj-1";
    internal const string V1ResourceName = "res-b";
    internal const string V1CustomerId = "4711";

    /// <summary>The customer-API URL v1 builds, minus the (escaped) search term.</summary>
    internal const string V1CustomerApiUrlPrefix =
        "https://cyclops.test.local/customerdbAPI/api/v1.0/customer?page=1&perPage=100&search=";

    /// <summary>A <c>customerdbAPI</c> response body carrying the given customers, in the shipped shape.</summary>
    internal static string CustomerJson(params (string Name, string CustomerId)[] customers)
    {
        var entries = customers.Select(customer =>
            "{\"Name\":\"" + customer.Name + "\",\"CustomerId\":\"" + customer.CustomerId + "\"}");
        return "{\"customers\":[" + string.Join(",", entries) + "]}";
    }

    /// <summary>The default lookup answer: one customer whose <c>Name</c> is <see cref="V1ProjectShortName"/>.</summary>
    internal static readonly string DefaultCustomerJson = CustomerJson((V1ProjectShortName, V1CustomerId));

    internal static ExternalServiceAccessCredentials Credentials(string? apiKey = VaultApiKey, string headerName = VaultHeaderName)
        => new()
        {
            Name = VaultCredentialName,
            CredentialType = ECredentialType.ApiKey,
            AuthorizationHeader = headerName,
            HeaderPrefix = string.Empty,
            ApiKey = apiKey
        };

    internal static Mock<IHashiCorpVaultClient> VaultReturning(ExternalServiceAccessCredentials credentials)
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault
            .Setup(v => v.GetExternalCredentials(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExternalServiceAccessCredentials, Error>.Success(credentials));
        return vault;
    }

    internal static Mock<IHashiCorpVaultClient> VaultFailing(string description)
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault
            .Setup(v => v.GetExternalCredentials(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExternalServiceAccessCredentials, Error>.Failure(
                new Error("Vault.FetchFailed", ErrorSeverity.Error, description)));
        return vault;
    }

    /// <summary>
    /// The container the forwarder's request transform resolves from: a logger, the bound
    /// <see cref="CyclopsOptions"/> and the vault client, plus the services
    /// <see cref="InternalTokenTransformExtensions.AddPermissionCheckTransform"/> resolves when a test
    /// drives that first transform as well.
    /// </summary>
    internal static ServiceProvider BuildServices(
        IHashiCorpVaultClient? vault = null,
        Uri? baseUrl = null,
        Uri? timeUsageRelativeUrl = null,
        Uri? totalUsageRelativeUrl = null,
        IHttpClientFactory? customerApi = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(vault ?? VaultReturning(Credentials()).Object);
        services.AddSingleton<IOptions<CyclopsOptions>>(Options.Create(new CyclopsOptions
        {
            BaseUrl = baseUrl ?? BaseUrl,
            TimeUsageRelativeUrl = timeUsageRelativeUrl ?? TimeUsageRelativeUrl,
            TotalUsageRelativeUrl = totalUsageRelativeUrl ?? TotalUsageRelativeUrl
        }));

        // Only the v1 route resolves this: it translates ProjectShortName -> CustomerId through the
        // "cyclops-customer" named client before it builds the Cyclops URL. The v2 routes never touch it,
        // which is what an empty StubCustomerApi.Calls after a v2 run proves.
        services.AddSingleton<IHttpClientFactory>(customerApi ?? StubCustomerApi.Returning(DefaultCustomerJson));

        // Only reached when a test drives RequestTransforms[0] (the permission check) as well.
        services.AddSingleton<IInternalTokenProvider>(new UnusedInternalTokenProvider());
        services.AddSingleton<IUserPermissionsCache>(new UnusedPermissionsCache());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds the request the gateway would hand the transform: the shipped path, the caller's query string,
    /// and a response whose body is captured so the 400/500 texts can be asserted.
    /// </summary>
    internal static DefaultHttpContext BuildHttpContext(
        string path,
        IServiceProvider services,
        string? fromDate = null,
        string? toDate = null,
        bool responseHasStarted = false)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(responseHasStarted ? new StartedResponseFeature() : new HttpResponseFeature());
        var body = new MemoryStream();
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));

        var httpContext = new DefaultHttpContext(features)
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };
        httpContext.Request.Path = path;

        var query = new List<string>();
        if (fromDate is not null)
        {
            query.Add($"fromDate={Uri.EscapeDataString(fromDate)}");
        }

        if (toDate is not null)
        {
            query.Add($"toDate={Uri.EscapeDataString(toDate)}");
        }

        if (query.Count > 0)
        {
            httpContext.Request.QueryString = new QueryString($"?{string.Join("&", query)}");
        }

        httpContext.Items[ResponseBodyItemKey] = body;
        return httpContext;
    }

    private const string ResponseBodyItemKey = "test.response.body";

    internal static string ReadResponseBody(HttpContext httpContext)
    {
        var body = (MemoryStream)httpContext.Items[ResponseBodyItemKey]!;
        return System.Text.Encoding.UTF8.GetString(body.ToArray());
    }

    /// <summary>Applies the shipped <c>/api/Cyclops/Usage/…</c> transform stack's request transform.</summary>
    internal static Task<CyclopsTransformRun> ApplyUsageAsync(HttpContext httpContext)
        => ApplyAsync(httpContext, static builderContext => builderContext.AddCyclopsUsageTransforms());

    /// <summary>Applies the shipped <c>/api/Other/CyclopsUsage/…</c> (v1) transform stack's request transform.</summary>
    internal static Task<CyclopsTransformRun> ApplyUsageV1Async(HttpContext httpContext)
        => ApplyAsync(httpContext, static builderContext => builderContext.AddCyclopsUsageV1Transforms());

    /// <summary>Applies the shipped <c>/api/Cyclops/TotalUsage/…</c> transform stack's request transform.</summary>
    internal static Task<CyclopsTransformRun> ApplyTotalUsageAsync(HttpContext httpContext)
        => ApplyAsync(httpContext, static builderContext => builderContext.AddCyclopsTotalUsageTransforms());

    private static async Task<CyclopsTransformRun> ApplyAsync(HttpContext httpContext, Action<TransformBuilderContext> configure)
    {
        var builderContext = new TransformBuilderContext();
        configure(builderContext);

        // Two request transforms: the permission check, then the Cyclops one. Index 1 is the one under test;
        // the ordering itself is asserted by CyclopsForwardingTransformTests.
        var requestContext = new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage()
        };

        await builderContext.RequestTransforms[^1].ApplyAsync(requestContext);
        return new CyclopsTransformRun(builderContext, requestContext, ReadResponseBody(httpContext));
    }

    /// <summary>Splits the built query into its raw (still percent-encoded) <c>name=value</c> parts.</summary>
    internal static IReadOnlyDictionary<string, string> RawQuery(Uri uri)
    {
        var raw = uri.Query.TrimStart('?');
        return raw.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : raw.Split('&')
                .Select(part => part.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : string.Empty, StringComparer.Ordinal);
    }

    /// <summary>An <see cref="IHttpResponseFeature"/> that reports the response as already started.</summary>
    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class UnusedInternalTokenProvider : IInternalTokenProvider
    {
        public Task<string?> GenerateInternalTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string systemRole, string? providerId, string? email = null, IDictionary<string, ICollection<string>>? projectRoles = null, string? providerName = null, string? originalJti = null)
            => throw new NotSupportedException();

        public Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience = "forge")
            => throw new NotSupportedException();

        public Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token)
            => throw new NotSupportedException();

        public string GetCurrentKeyId() => throw new NotSupportedException();
    }

    private sealed class UnusedPermissionsCache : IUserPermissionsCache
    {
        public Task<UserPermissionsSnapshot> GetAsync(string idpUserId, CancellationToken ct)
            => throw new NotSupportedException();
        public Task InvalidateAsync(string idpUserId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task InvalidateAllAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }
}

/// <summary>The result of one transform application: the builder, the proxy request and the response body.</summary>
internal sealed record CyclopsTransformRun(
    TransformBuilderContext BuilderContext,
    RequestTransformContext RequestContext,
    string ResponseBody)
{
    internal HttpRequestMessage ProxyRequest => RequestContext.ProxyRequest;

    internal HttpContext HttpContext => RequestContext.HttpContext;

    internal int StatusCode => RequestContext.HttpContext.Response.StatusCode;

    internal Uri? RequestUri => RequestContext.ProxyRequest.RequestUri;
}

/// <summary>
/// One outbound call the forwarder made through <see cref="IHttpClientFactory"/>, captured at send time: the
/// production code disposes its <see cref="HttpRequestMessage"/>, so nothing may be read from it afterwards.
/// </summary>
internal sealed record CustomerApiCall(string ClientName, Uri? RequestUri, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    internal string? HeaderValue(string name)
        => Headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>
/// The seam for the v1 <c>customerdbAPI</c> lookup: an <see cref="IHttpClientFactory"/> whose clients answer
/// from a canned response (or throw), recording every request. The v2 routes resolve no factory at all, so an
/// empty <see cref="Calls"/> after a v2 run is what proves the lookup is v1-only.
/// </summary>
internal sealed class StubCustomerApi : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

    private StubCustomerApi(Func<HttpRequestMessage, HttpResponseMessage> respond) => this.respond = respond;

    internal List<CustomerApiCall> Calls { get; } = [];

    internal static StubCustomerApi Returning(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    internal static StubCustomerApi Throwing(Exception exception) => new(_ => throw exception);

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The handler is owned by the returned HttpClient (disposeHandler: true), which mirrors what IHttpClientFactory hands the production code.")]
    [SuppressMessage(
        "IDisposableAnalyzers.Correctness",
        "IDISP004:Don't ignore created IDisposable",
        Justification = "The handler is owned by the returned HttpClient (disposeHandler: true), which mirrors what IHttpClientFactory hands the production code.")]
    public HttpClient CreateClient(string name) => new(new StubHandler(this, name), disposeHandler: true);

    private sealed class StubHandler(StubCustomerApi owner, string clientName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Calls.Add(new CustomerApiCall(
                clientName,
                request.RequestUri,
                request.Headers.Select(header => new KeyValuePair<string, string>(header.Key, string.Join(",", header.Value))).ToList()));

            return Task.FromResult(owner.respond(request));
        }
    }
}
