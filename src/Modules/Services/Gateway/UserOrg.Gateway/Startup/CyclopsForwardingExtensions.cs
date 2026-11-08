using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using HashiCorpVault.Contracts;

using Microsoft.Extensions.Options;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Startup;

// Response DTOs for the v1 Cyclops customer API lookup (customerdbAPI), restored byte for byte from
// origin/main's pre-MR !389 shape. The v1 route is kept for backward compatibility.
internal record CyclopsCustomer(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("CustomerId")] string CustomerId
);

internal record CyclopsCustomerResponse(
    [property: JsonPropertyName("customers")] List<CyclopsCustomer> Customers
);

internal static class CyclopsForwardingExtensions
{
    private const string cyclopsProjectUsagePrefix = "/api/Cyclops/Usage/";
    private const string cyclopsTotalUsagePrefix = "/api/Cyclops/TotalUsage/";
    private const string cyclopsUsageV1Prefix = "/api/Other/CyclopsUsage/";
    private const string cyclopsDestination = "https+http://cyclops-api";
    private const string cyclopsServiceConfigurationErrorText = "Server is not correctly configured.";
    private const string cyclopsServiceCommunicationErrorText = "Error communicating with Cyclops service. Please try again later.";
    private const string cyclopsInvalidDateRangeErrorText = "Bad Request: toDate must be after fromDate.";

    /// <summary>The v1 400 body, verbatim from pre-MR <c>main</c> — ends in <c>/...</c>, unlike v2's message.</summary>
    private const string cyclopsUsageV1ExpectedPathText = "Bad Request: expected /api/Other/CyclopsUsage/{project}/{resource}/...";

    private const string cyclopsUsageV1VaultFailureErrorText = "Server secrets configuration error";
    private const string cyclopsUsageV1ApiKeyMissingErrorText = "Server configuration error";
    private const string cyclopsUsageV1CustomerLookupErrorText = "Failed to retrieve customer information from Cyclops";
    private const string cyclopsUsageV1CommunicationErrorText = "Error communicating with Cyclops service";
    private const string cyclopsUsageV1ResponseProcessingErrorText = "Error processing Cyclops response";

    /// <summary>
    /// Parsed once and reused: the message is formatted on every malformed date bound, and re-parsing the
    /// placeholder each time is the exact waste <c>CompositeFormat</c> exists to avoid (CA1863).
    /// </summary>
    private static readonly CompositeFormat cyclopsInvalidDateErrorFormat =
        CompositeFormat.Parse("Bad Request: {0} must be a valid ISO-8601 date.");

    extension(IEndpointRouteBuilder app)
    {
        /// <summary>
        /// Forwards <c>/api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}</c> to the Cyclops
        /// per-record usage endpoint (<see cref="CyclopsOptions.TimeUsageRelativeUrl"/>), filtered by the
        /// project (<c>idlist</c>) and the resource (<c>metadata</c>).
        /// </summary>
        public void MapCyclopsUsageForwarder()
            => app.MapCyclopsForwarder(
                cyclopsProjectUsagePrefix,
                static transformBuilder => transformBuilder.AddCyclopsUsageTransforms());

        /// <summary>
        /// Forwards <c>/api/Cyclops/TotalUsage/{ProviderResourceName}</c> to the Cyclops aggregate endpoint
        /// (<see cref="CyclopsOptions.TotalUsageRelativeUrl"/>), filtered by the resource (<c>metadata</c>)
        /// only — the aggregation spans every project the resource is used by.
        /// </summary>
        public void MapCyclopsTotalUsageForwarder()
            => app.MapCyclopsForwarder(
                cyclopsTotalUsagePrefix,
                static transformBuilder => transformBuilder.AddCyclopsTotalUsageTransforms());

        /// <summary>
        /// Forwards <c>/api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}</c> — the v1 route, kept for
        /// backward compatibility byte for byte as it behaved on <c>main</c> before MR !389. Unlike the v2
        /// routes, it resolves a <c>CustomerId</c> through the <c>customerdbAPI</c> lookup and never filters
        /// by resource: the second path segment is parsed and discarded.
        /// </summary>
        public void MapCyclopsUsageV1Forwarder()
            => app.MapCyclopsForwarder(
                cyclopsUsageV1Prefix,
                static transformBuilder => transformBuilder.AddCyclopsUsageV1Transforms());

        /// <summary>
        /// Maps the catch-all forwarder route this forwarder owns and hands YARP the transform
        /// configurator that authorizes the caller, rewrites the request to Cyclops and injects the API key.
        /// </summary>
        /// <param name="pathPrefix">Gateway path prefix owned by this forwarder (trailing slash included).</param>
        /// <param name="configureTransforms">Applies this forwarder's transforms to YARP's builder context.</param>
        private void MapCyclopsForwarder(string pathPrefix, Action<TransformBuilderContext> configureTransforms)
            => app.MapForwarder($"{pathPrefix}{{**catch-all}}", cyclopsDestination, configureTransforms);
    }

    /// <summary>
    /// The production argument set of the <c>/api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}</c>
    /// route. Called by <c>MapCyclopsUsageForwarder</c> and, so a test drives the shipped prefix, relative-URL
    /// selector and query builder rather than a copy of them, by the tests.
    /// </summary>
    internal static void AddCyclopsUsageTransforms(this TransformBuilderContext transformBuilder)
        => transformBuilder.AddCyclopsForwardingTransforms(
            cyclopsProjectUsagePrefix,
            options => options.TimeUsageRelativeUrl,
            expectedPathText: $"{cyclopsProjectUsagePrefix}{{ProjectShortName}}/{{ProviderResourceName}}",
            requiredSegments: 2,
            buildQuery: (queryParts, segments) =>
            {
                queryParts.Add($"idlist={Uri.EscapeDataString(segments[0])}");
                queryParts.Add($"metadata=LexisResourceName:{Uri.EscapeDataString(segments[1])}");
            });

    /// <summary>
    /// The production argument set of the <c>/api/Cyclops/TotalUsage/{ProviderResourceName}</c> route.
    /// Called by <c>MapCyclopsTotalUsageForwarder</c> and by the tests, for the same reason.
    /// </summary>
    internal static void AddCyclopsTotalUsageTransforms(this TransformBuilderContext transformBuilder)
        => transformBuilder.AddCyclopsForwardingTransforms(
            cyclopsTotalUsagePrefix,
            options => options.TotalUsageRelativeUrl,
            expectedPathText: $"{cyclopsTotalUsagePrefix}{{ProviderResourceName}}",
            requiredSegments: 1,
            buildQuery: (queryParts, segments) =>
                queryParts.Add($"metadata=LexisResourceName:{Uri.EscapeDataString(segments[0])}"));

    /// <summary>
    /// Adds the permission check, the request transform that rewrites the request to the Cyclops API endpoint
    /// selected by <paramref name="relativeUrlSelector"/> and injects the API key taken from the vault, and the
    /// response transform that strips that key from the response.
    /// </summary>
    /// <param name="transformBuilder">YARP's transform builder context for this route.</param>
    /// <param name="pathPrefix">Gateway path prefix owned by this forwarder (trailing slash included).</param>
    /// <param name="relativeUrlSelector">Picks the Cyclops endpoint from the configured options.</param>
    /// <param name="expectedPathText">Path template reported to the client on a malformed request.</param>
    /// <param name="requiredSegments">Number of path segments expected after <paramref name="pathPrefix"/>.</param>
    /// <param name="buildQuery">Appends the Cyclops filter parameters for the parsed path segments.</param>
    internal static void AddCyclopsForwardingTransforms(
        this TransformBuilderContext transformBuilder,
        string pathPrefix,
        Func<CyclopsOptions, Uri?> relativeUrlSelector,
        string expectedPathText,
        int requiredSegments,
        Action<List<string>, string[]> buildQuery)
    {
        var route = new CyclopsUsageRoute(pathPrefix, relativeUrlSelector, expectedPathText, requiredSegments, buildQuery);
        var apiKeyHeader = new ApiKeyHeaderSlot();

        transformBuilder.AddPermissionCheckTransform();
        transformBuilder.AddRequestTransform(context =>
            ForwardUnlessResponseStartedAsync(context, services => ForwardUsageRequestAsync(context, services, route, apiKeyHeader)));

        // Response transform: remove any X-Api-Key header from response to avoid leaking the secret back to client
        transformBuilder.AddResponseTransform(context => RemoveApiKeyHeaderAsync(context, apiKeyHeader));
    }

    private static async ValueTask ForwardUsageRequestAsync(
        RequestTransformContext context, CyclopsServices services, CyclopsUsageRoute route, ApiKeyHeaderSlot apiKeyHeader)
    {
        var httpCtx = context.HttpContext;
        var (logger, options, _) = services;

        var segments = PathSegmentsAfter(httpCtx.Request.Path.Value, route.PathPrefix);
        if (segments.Length != route.RequiredSegments)
        {
            await WriteErrorAsync(httpCtx, StatusCodes.Status400BadRequest, $"Bad Request: expected {route.ExpectedPathText}.");
            return;
        }

        var from = httpCtx.Request.Query["fromDate"].ToString();
        var to = httpCtx.Request.Query["toDate"].ToString();

        var dateRangeError = DateRangeError(from, to);
        if (dateRangeError is not null)
        {
            await WriteErrorAsync(httpCtx, StatusCodes.Status400BadRequest, dateRangeError);
            return;
        }

        // relativeUrlSelector reads a required, non-nullable Uri member of CyclopsOptions: it is never
        // null in practice, but the selector's signature stays Func<CyclopsOptions, Uri?> for the tests.
        var relativeUrl = route.RelativeUrlSelector(options)!;

        var apiKey = await ResolveApiKeyAsync(
            context, services, apiKeyHeader, cyclopsServiceConfigurationErrorText, cyclopsServiceConfigurationErrorText);
        if (apiKey is null)
        {
            return;
        }

        var cyclopsApiBaseUri = new Uri(options.BaseUrl, relativeUrl);
        try
        {
            var queryParts = DateQueryParts(from, to);
            route.BuildQuery(queryParts, segments);

            var ub = new UriBuilder(cyclopsApiBaseUri)
            {
                Query = string.Join("&", queryParts)
            };

            context.ProxyRequest.RequestUri = ub.Uri;
            SetApiKeyHeader(context.ProxyRequest, apiKeyHeader.Name, apiKey);

            logger.LogCyclopsForwardPrepared(cyclopsApiBaseUri.AbsoluteUri);
        }
        catch (HttpRequestException ex)
        {
            logger.LogCyclopsApiCallError(ex, cyclopsApiBaseUri.AbsoluteUri);
            await WriteErrorAsync(httpCtx, StatusCodes.Status500InternalServerError, cyclopsServiceCommunicationErrorText);
        }
    }

    private static async ValueTask ForwardUnlessResponseStartedAsync(RequestTransformContext context, Func<CyclopsServices, ValueTask> forward)
    {
        var services = CyclopsServices.Resolve(context.HttpContext);

        // Permission check already handled by AddPermissionCheckTransform above;
        if (context.HttpContext.Response.HasStarted)
        {
            return;
        }

        await forward(services);
    }

    // Both bounds are optional and independent: either one, both, or neither may be sent.
    // Whatever arrives must be a well formed date, and a full range must not be inverted —
    // checked before we spend a vault lookup and an upstream call on it.
    private static string? DateRangeError(string from, string to)
    {
        if (!TryParseOptionalDate(from, out var fromDate))
        {
            return InvalidDateError("fromDate");
        }

        if (!TryParseOptionalDate(to, out var toDate))
        {
            return InvalidDateError("toDate");
        }

        return IsInvertedRange(fromDate, toDate) ? cyclopsInvalidDateRangeErrorText : null;
    }

    private static string InvalidDateError(string parameterName) =>
        string.Format(CultureInfo.InvariantCulture, cyclopsInvalidDateErrorFormat, parameterName);

    private static bool TryParseOptionalDate(string value, out DateTimeOffset? date)
    {
        date = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (!TryParseDate(value, out var parsed))
        {
            return false;
        }

        date = parsed;
        return true;
    }

    // Only comparable when the caller sent both ends of the range.
    private static bool IsInvertedRange(DateTimeOffset? fromDate, DateTimeOffset? toDate) =>
        fromDate is { } rangeStart && toDate is { } rangeEnd && rangeEnd <= rangeStart;

    private static List<string> DateQueryParts(string from, string to)
    {
        var queryParts = new List<string>();

        if (!string.IsNullOrEmpty(from))
        {
            queryParts.Add($"from={Uri.EscapeDataString(from)}");
        }

        if (!string.IsNullOrEmpty(to))
        {
            queryParts.Add($"to={Uri.EscapeDataString(to)}");
        }

        return queryParts;
    }

    /// <summary>
    /// The production argument set of the <c>/api/Other/CyclopsUsage/{ProjectShortName}/{ResourceName}</c>
    /// v1 route. Called by <c>MapCyclopsUsageV1Forwarder</c> and by the tests. Restored byte for byte from
    /// <c>origin/main</c>'s pre-MR !389 shape: no date parsing, no range check, a <c>customerdbAPI</c>
    /// <c>CustomerId</c> lookup instead of a translated <c>idlist</c>, and five distinct error bodies. Do not
    /// align any of this with <see cref="AddCyclopsForwardingTransforms"/> — that is the v2 behaviour and the
    /// two must not converge.
    /// </summary>
    internal static void AddCyclopsUsageV1Transforms(this TransformBuilderContext transformBuilder)
    {
        var apiKeyHeader = new ApiKeyHeaderSlot();

        transformBuilder.AddPermissionCheckTransform();
        transformBuilder.AddRequestTransform(context =>
            ForwardUnlessResponseStartedAsync(context, services => ForwardUsageV1RequestAsync(context, services, apiKeyHeader)));

        // Response transform: remove any X-Api-Key header from response to avoid leaking the secret back to client
        transformBuilder.AddResponseTransform(context => RemoveApiKeyHeaderAsync(context, apiKeyHeader));
    }

    private static async ValueTask ForwardUsageV1RequestAsync(RequestTransformContext context, CyclopsServices services, ApiKeyHeaderSlot apiKeyHeader)
    {
        var httpCtx = context.HttpContext;
        var (logger, options, _) = services;

        // v1 tolerates surplus path segments and ignores the resource name entirely — < 2, never != 2.
        var segments = PathSegmentsAfter(httpCtx.Request.Path.Value, cyclopsUsageV1Prefix);
        if (segments.Length < 2)
        {
            await WriteErrorAsync(httpCtx, StatusCodes.Status400BadRequest, cyclopsUsageV1ExpectedPathText);
            return;
        }

        var projectShortName = segments[0];

        var apiKey = await ResolveApiKeyAsync(
            context, services, apiKeyHeader, cyclopsUsageV1VaultFailureErrorText, cyclopsUsageV1ApiKeyMissingErrorText);
        if (apiKey is null)
        {
            return;
        }

        var cyclopsBase = options.BaseUrl;
        var customerApiUrl = new Uri(cyclopsBase, $"/customerdbAPI/api/v1.0/customer?page=1&perPage=100&search={Uri.EscapeDataString(projectShortName)}");

        var httpClientFactory = httpCtx.RequestServices.GetRequiredService<IHttpClientFactory>();
        var customerHttpClient = httpClientFactory.CreateClient("cyclops-customer");

        try
        {
            using var customerRequest = new HttpRequestMessage(HttpMethod.Get, customerApiUrl);
            customerRequest.Headers.TryAddWithoutValidation(apiKeyHeader.Name, apiKey);
            var customerResponse = await customerHttpClient.SendAsync(customerRequest, context.CancellationToken);

            var customerId = await ReadCustomerIdAsync(context, customerResponse, customerApiUrl, projectShortName, logger);
            if (customerId is null)
            {
                return;
            }

            // Determine Cyclops base URL and relative URL from options; v1 reuses TimeUsageRelativeUrl —
            // it and pre-MR v1's own RelativeUrl both default to the same Cyclops endpoint.
            var target = new UriBuilder(new Uri(cyclopsBase, options.TimeUsageRelativeUrl))
            {
                Query = UsageV1Query(httpCtx.Request.Query, customerId)
            }.Uri;

            context.ProxyRequest.RequestUri = target;
            SetApiKeyHeader(context.ProxyRequest, apiKeyHeader.Name, apiKey);

            logger.LogCyclopsForwardPrepared(target.AbsoluteUri);
        }
        catch (HttpRequestException ex)
        {
            logger.LogCyclopsApiCallError(ex, customerApiUrl.AbsoluteUri);
            await WriteErrorAsync(httpCtx, StatusCodes.Status500InternalServerError, cyclopsUsageV1CommunicationErrorText);
        }
        catch (JsonException ex)
        {
            logger.LogCyclopsCustomerResponseDeserializationError(ex, customerApiUrl.AbsoluteUri);
            await WriteErrorAsync(httpCtx, StatusCodes.Status500InternalServerError, cyclopsUsageV1ResponseProcessingErrorText);
        }
    }

    private static async Task<string?> ReadCustomerIdAsync(
        RequestTransformContext context, HttpResponseMessage customerResponse, Uri customerApiUrl, string projectShortName, ILogger logger)
    {
        if (!customerResponse.IsSuccessStatusCode)
        {
            logger.LogCyclopsCustomerLookupFailure(customerApiUrl.AbsoluteUri, (int)customerResponse.StatusCode);
            await WriteErrorAsync(context.HttpContext, StatusCodes.Status500InternalServerError, cyclopsUsageV1CustomerLookupErrorText);
            return null;
        }

        var jsonContent = await customerResponse.Content.ReadAsStringAsync(context.CancellationToken);
        var customerData = JsonSerializer.Deserialize<CyclopsCustomerResponse>(jsonContent);

        return await SelectCustomerIdAsync(context.HttpContext, customerData?.Customers, projectShortName, logger);
    }

    private static async Task<string?> SelectCustomerIdAsync(
        HttpContext httpCtx, List<CyclopsCustomer>? customers, string projectShortName, ILogger logger)
    {
        if (customers == null || customers.Count == 0)
        {
            logger.LogCyclopsCustomerNotFound(projectShortName);
            await WriteCustomerNotFoundAsync(httpCtx, projectShortName);
            return null;
        }

        var customer = customers.FirstOrDefault(c =>
            string.Equals(c.Name, projectShortName, StringComparison.OrdinalIgnoreCase));

        if (customer == null)
        {
            logger.LogCyclopsCustomerNameNotMatched(projectShortName);
            await WriteCustomerNotFoundAsync(httpCtx, projectShortName);
            return null;
        }

        logger.LogCyclopsCustomerResolved(customer.CustomerId, projectShortName);
        return customer.CustomerId;
    }

    private static Task WriteCustomerNotFoundAsync(HttpContext httpCtx, string projectShortName) =>
        WriteErrorAsync(httpCtx, StatusCodes.Status404NotFound, $"Customer not found for project: {projectShortName}");

    // Build query: preserve optional from/to (present-but-empty forwards as an empty parameter —
    // v1 tests presence with TryGetValue, not emptiness, unlike v2) and append idlist with CustomerId
    private static string UsageV1Query(IQueryCollection query, string customerId)
    {
        var queryParts = new List<string>();
        if (query.TryGetValue("fromDate", out var from))
        {
            queryParts.Add($"from={Uri.EscapeDataString(from.ToString())}");
        }

        if (query.TryGetValue("toDate", out var to))
        {
            queryParts.Add($"to={Uri.EscapeDataString(to.ToString())}");
        }

        queryParts.Add($"idlist={Uri.EscapeDataString(customerId)}");
        return string.Join("&", queryParts);
    }

    private static string[] PathSegmentsAfter(string? fullPath, string pathPrefix)
    {
        var path = fullPath ?? string.Empty;
        var remainder = path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)
            ? path.Substring(pathPrefix.Length)
            : string.Empty;

        return remainder.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Reads the Cyclops credentials from the vault and records their header name in <paramref name="apiKeyHeader"/>.
    /// Returns the API key, or <see langword="null"/> after writing a 500 response when the vault lookup fails or
    /// returns no key.
    /// </summary>
    private static async Task<string?> ResolveApiKeyAsync(
        RequestTransformContext context,
        CyclopsServices services,
        ApiKeyHeaderSlot apiKeyHeader,
        string vaultFailureText,
        string apiKeyMissingText)
    {
        var cyclopsCredentialsResult = await services.Vault.GetExternalCredentials("cyclops", context.CancellationToken);
        if (!cyclopsCredentialsResult.IsSuccess)
        {
            services.Logger.LogCyclopsVaultCredentialsRetrievalFailure(cyclopsCredentialsResult.Error.Description);
            await WriteErrorAsync(context.HttpContext, StatusCodes.Status500InternalServerError, vaultFailureText);
            return null;
        }

        var apiKey = cyclopsCredentialsResult.Value.ApiKey;
        apiKeyHeader.Name = cyclopsCredentialsResult.Value.AuthorizationHeader;
        if (string.IsNullOrEmpty(apiKey))
        {
            services.Logger.LogCyclopsApiKeyNotConfigured();
            await WriteErrorAsync(context.HttpContext, StatusCodes.Status500InternalServerError, apiKeyMissingText);
            return null;
        }

        return apiKey;
    }

    private static void SetApiKeyHeader(HttpRequestMessage proxyRequest, string headerName, string apiKey)
    {
        proxyRequest.Headers.Remove(headerName);
        proxyRequest.Headers.TryAddWithoutValidation(headerName, apiKey);
    }

    private static ValueTask RemoveApiKeyHeaderAsync(ResponseTransformContext context, ApiKeyHeaderSlot apiKeyHeader)
    {
        context.HttpContext.Response.Headers.Remove(apiKeyHeader.Name);
        return ValueTask.CompletedTask;
    }

    private static Task WriteErrorAsync(HttpContext httpCtx, int statusCode, string message)
    {
        httpCtx.Response.StatusCode = statusCode;
        return httpCtx.Response.WriteAsync(message);
    }

    /// <summary>The request services both Cyclops request transforms resolve, in the order they always have.</summary>
    private sealed record CyclopsServices(ILogger Logger, CyclopsOptions Options, IHashiCorpVaultClient Vault)
    {
        public static CyclopsServices Resolve(HttpContext httpCtx) => new(
            httpCtx.RequestServices.GetRequiredService<ILogger<Program>>(),
            httpCtx.RequestServices.GetRequiredService<IOptions<CyclopsOptions>>().Value,
            httpCtx.RequestServices.GetRequiredService<IHashiCorpVaultClient>());
    }

    /// <summary>The per-route arguments of <see cref="AddCyclopsForwardingTransforms"/>.</summary>
    private sealed record CyclopsUsageRoute(
        string PathPrefix,
        Func<CyclopsOptions, Uri?> RelativeUrlSelector,
        string ExpectedPathText,
        int RequiredSegments,
        Action<List<string>, string[]> BuildQuery);

    /// <summary>
    /// The credential header name the request transform read from the vault, shared with the response transform
    /// of the same route so it strips that header from the response.
    /// </summary>
    private sealed class ApiKeyHeaderSlot
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// The ISO-8601 shapes the error message promises: a bare date, and the three round-trip forms with
    /// second, minute or fractional-second precision and an explicit offset or <c>Z</c>.
    /// </summary>
    private static readonly string[] cyclopsIsoDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mmK",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
    ];

    /// <summary>
    /// Parses a date bound exactly as the caller sent it — invariant culture, no local-time assumption — so a
    /// range is compared the same way regardless of the server's locale or time zone. Restricted to ISO-8601
    /// forms: <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// also accepts locale-ambiguous strings like <c>12/31/2024</c>, which the error message does not promise.
    /// </summary>
    private static bool TryParseDate(string value, out DateTimeOffset date) =>
        DateTimeOffset.TryParseExact(
            value,
            cyclopsIsoDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out date);
}