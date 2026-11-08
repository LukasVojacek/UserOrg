using System.Text;
using System.Text.Json;

using Heappe.Contracts.Connection;
using Heappe.Helpers.Connection;

using Microsoft.AspNetCore.WebUtilities;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

namespace Heappe.Helpers;

/// <summary>
/// Fluent builder for constructing <see cref="HttpRequestMessage"/> instances targeting HEAppE API endpoints.
/// Supports configurable HTTP method, URI, query parameters, headers, and content (form-encoded or custom).
/// </summary>
internal sealed class HeappeHttpRequestBuilder : IDisposable
{
    private bool _disposed;
    public static readonly JsonSerializerOptions DefaultJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string _serviceApiKeyHeaderName = "X-API-Key";
    private const string _authorizationHeaderName = "Authorization";

    private HttpMethod _method = HttpMethod.Get;

    private Uri? _baseAddress;
    private string? _relativePath;

    private readonly List<KeyValuePair<string, string>> _queryParameters = [];
    private readonly List<KeyValuePair<string, string>> _headers = [];
    private readonly List<KeyValuePair<string, string>> _bodyContentFields = [];
    private HttpContent? _bodyContent;

    private string? _sessionCode;

    private HeappeIdentifier? _identifier;

    /// <summary>
    /// Request option carrying the HEAppE identifier. Every instance sits behind the same Nexus host, so the data
    /// client keys its circuit breaker by this option instead of by host.
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> HeappeIdentifierKey = new("Heappe.Identifier");

    /// <summary>
    /// Names the HEAppE instance the request is for; <see cref="Build"/> puts it into <see cref="HeappeIdentifierKey"/>.
    /// </summary>
    /// <param name="identifier">The instance the request is addressed to.</param>
    /// <returns>The current builder instance for chaining.</returns>
    public HeappeHttpRequestBuilder WithInstance(HeappeIdentifier identifier)
    {
        _identifier = identifier;
        return this;
    }

    /// <summary>
    /// Sets the HTTP method for the request.
    /// </summary>
    /// <param name="method">The HTTP method to use (e.g., GET, POST, PUT, DELETE).</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="method"/> is null.</exception>
    public HeappeHttpRequestBuilder WithMethod(HttpMethod method)
    {
        _method = method ?? throw new ArgumentNullException(nameof(method));
        return this;
    }

    /// <summary>
    /// Sets the base address (scheme + host + optional port) for the request URI. Trailing slashes are trimmed.
    /// </summary>
    /// <param name="baseAddress">The base address (e.g., "https://api.example.com").</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseAddress"/> is null or whitespace.</exception>
    public HeappeHttpRequestBuilder WithBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        _baseAddress = baseAddress.ToNormalizeUrl();
        return this;
    }

    /// <summary>
    /// Sets both the base address and the relative path that will be appended to it.
    /// Trailing slashes from the base address and leading slashes from the relative path are trimmed.
    /// </summary>
    /// <param name="baseAddress">The base address (e.g., "https://api.example.com").</param>
    /// <param name="relativePath">The relative path (e.g., "heappe/Management/CommandTemplates").</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either parameter is null or whitespace.</exception>
    public HeappeHttpRequestBuilder WithBaseAddressAndPath(Uri baseAddress, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(relativePath);

        _baseAddress = baseAddress.ToNormalizeUrl();
        if (!string.IsNullOrEmpty(relativePath))
        {
            _relativePath = relativePath.TrimStart('/');
        }

        return this;
    }

    /// <summary>
    /// Configures the request builder to use authorization information from the specified connection info.
    /// </summary>
    /// <remarks>Supported connection info types include <see cref="ServiceTokenHeappeConnectionInfo"/>, <see
    /// cref="BearerTokenHeappeConnectionInfo"/>, and <see cref="SessionCodeHEAppEConnectionInfo"/>. Each type
    /// configures authorization differently based on its credentials.</remarks>
    /// <param name="connectionInfo">The connection information containing credentials or tokens used to authorize requests. Cannot be null. Must be
    /// an instance of a supported connection info type.</param>
    /// <returns>The current instance of <see cref="HeappeHttpRequestBuilder"/> with authorization configured based on the
    /// provided connection info.</returns>
    /// <exception cref="NotSupportedException">Thrown if <paramref name="connectionInfo"/> is not of a supported type.</exception>
    public HeappeHttpRequestBuilder WithAuthorization(BaseHeappeConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);
        WithInstance(connectionInfo.Identifier);
        _ = connectionInfo switch
        {
            ServiceTokenHeappeConnectionInfo serviceConnectionInfo => WithServiceTokenAuthorization(serviceConnectionInfo.ServiceToken),
            BearerTokenHeappeConnectionInfo bearerConnectionInfo => WithBearerTokenAuthorization(bearerConnectionInfo.BearerToken),
            SessionCodeHEAppEConnectionInfo sessionCodeConnectionInfo => WithSessionCodeAuthorization(sessionCodeConnectionInfo.SessionCode),
            PublicHeappeConnectionInfo publicHeappeConnectionInfo => this, // No authorization needed for public connection info
            _ => throw new NotSupportedException($"Unsupported connection info type: {connectionInfo.GetType().FullName}")
        };

        return this;
    }

    /// <summary>
    /// Adds a service token authorization header to the HTTP request builder.
    /// </summary>
    /// <remarks>Use this method to include a service token in the request headers for APIs that require
    /// token-based authentication. Subsequent calls will add additional headers; ensure only one token header is
    /// present if required by the target API.</remarks>
    /// <param name="token">The service token to use for authorization. Cannot be null, empty, or consist solely of whitespace.</param>
    /// <returns>The current instance of <see cref="HeappeHttpRequestBuilder"/> with the service token authorization header
    /// added.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="token"/> is null, empty, or consists only of whitespace.</exception>
    public HeappeHttpRequestBuilder WithServiceTokenAuthorization(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentNullException(nameof(token));
        }

        _headers.Add(new KeyValuePair<string, string>(_serviceApiKeyHeaderName, $"{token}"));
        return this;
    }

    /// <summary>
    /// Adds a bearer token Authorization header to the request.
    /// </summary>
    /// <param name="token">The bearer token value (without "Bearer " prefix).</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token"/> is null or whitespace.</exception>
    public HeappeHttpRequestBuilder WithBearerTokenAuthorization(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentNullException(nameof(token));
        }

        _headers.Add(new KeyValuePair<string, string>(_authorizationHeaderName, $"Bearer {token}"));
        return this;
    }

    /// <summary>
    /// Sets a HEAppE session code which will be included in the query string for GET requests
    /// or in the request content for other HTTP methods.
    /// </summary>
    /// <param name="sessionCode">The session code provided by HEAppE.</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessionCode"/> is null or whitespace.</exception>
    public HeappeHttpRequestBuilder WithSessionCodeAuthorization(string sessionCode)
    {
        if (string.IsNullOrWhiteSpace(sessionCode))
        {
            throw new ArgumentNullException(nameof(sessionCode));
        }

        _sessionCode = sessionCode;
        return this;
    }

    /// <summary>
    /// Adds a single query parameter to the request URI.
    /// </summary>
    /// <param name="key">The query parameter key.</param>
    /// <param name="value">The query parameter value (may be null).</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    public HeappeHttpRequestBuilder WithQueryParameter(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        _queryParameters.Add(new KeyValuePair<string, string>(key, value ?? ""));
        return this;
    }

    /// <summary>
    /// Adds multiple query parameters to the request URI. Parameters with null or whitespace keys are filtered out.
    /// </summary>
    /// <param name="queryParameters">A collection of key-value pairs representing query parameters.</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryParameters"/> is null or results in no valid entries after filtering.</exception>
    public HeappeHttpRequestBuilder WithQueryParameters(IEnumerable<KeyValuePair<string, string?>> queryParameters)
    {
        var queryParams = queryParameters.ToList();

        if (queryParameters is null || queryParams.Count == 0)
        {
            return this;
        }

        var filteredQueryParameters = queryParams.Where(w => !string.IsNullOrWhiteSpace(w.Key));

        if (!filteredQueryParameters.Any())
        {
            throw new ArgumentNullException(nameof(queryParameters));
        }

        AddQueryParametersWithValidation(_queryParameters, filteredQueryParameters);

        return this;
    }

    /// <summary>
    /// Adds a custom header to the request. The header will be applied to the <see cref="HttpRequestMessage"/>
    /// when <see cref="Build"/> is called.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public HeappeHttpRequestBuilder WithHeader(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        _headers.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    /// <summary>
    /// Sets explicit <see cref="HttpContent"/> for the request. If set, this overrides any content fields.
    /// The builder will dispose this content when the builder is disposed.
    /// </summary>
    /// <param name="content">The HTTP content to use for the request body.</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    public HeappeHttpRequestBuilder WithBody(HttpContent content)
    {
        _bodyContent = content ?? throw new ArgumentNullException(nameof(content));
        return this;
    }

    /// <summary>
    /// Sets the HTTP request content to the specified object, serialized as JSON.      
    /// </summary>
    /// <remarks>The object is serialized using the configured JSON serialization options. This method
    /// replaces any previously set content.</remarks>
    /// <typeparam name="T">The type of the object to serialize as the request content.</typeparam>
    /// <param name="contentObj">The object to serialize and use as the HTTP request content. Cannot be null.</param>
    /// <returns>The current <see cref="HeappeHttpRequestBuilder"/> instance with the updated content.</returns>
    public HeappeHttpRequestBuilder WithBody<T>(T contentObj)
    {
        if (contentObj is HttpContent httpContent)
        {
            _bodyContent = httpContent;
            return this;
        }

        var json = JsonSerializer.Serialize(contentObj, DefaultJsonOptions);
        _bodyContent = new StringContent(json, Encoding.UTF8, "application/json");
        return this;
    }

    /// <summary>
    /// Adds content fields that are sent as one JSON object (<c>{"key":"value",...}</c>, not form-encoded) when the
    /// request is built (unless explicit <see cref="HttpContent"/> was already provided via <see cref="WithBody(HttpContent)"/>).
    /// Fields with null or whitespace keys or empty values are filtered out.
    /// </summary>
    /// <param name="contentFields">A collection of key-value pairs representing form fields.</param>
    /// <returns>The current builder instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentFields"/> is null or results in no valid entries after filtering.</exception>
    public HeappeHttpRequestBuilder WithBodyAsPairs(IEnumerable<KeyValuePair<string, string>> contentFields)
    {
        var filteredContentFields = contentFields.Where(w => !string.IsNullOrWhiteSpace(w.Key) && !string.IsNullOrEmpty(w.Value));

        if (!filteredContentFields.Any())
        {
            throw new ArgumentNullException(nameof(contentFields));
        }

        _bodyContentFields.AddRange(filteredContentFields);
        return this;
    }



    /// <summary>
    /// Builds the <see cref="HttpRequestMessage"/> using the configured values.
    /// <para>
    /// Behavior:
    /// - Query parameters are encoded into the URI using <see cref="QueryHelpers.AddQueryString"/>.
    /// - For GET requests, session code (if provided) is added to the query string.
    /// - For non-GET requests, session code (if provided) is added to the content fields.
    /// - If explicit content was provided via <see cref="WithBody(HttpContent)"/>, it is used as-is.
    /// - Otherwise, if content fields exist, they are sent as one JSON object.
    /// - Headers are applied to the request; if a header cannot be added to request headers, it is added to content headers (if content exists).
    /// </para>
    /// </summary>
    /// <returns>A configured <see cref="HttpRequestMessage"/> ready to be sent.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the base address has not been set.</exception>
    public HttpRequestMessage Build()
    {
        if (_baseAddress is null)
        {
            throw new InvalidOperationException("Base address is not set.");
        }

        AddSessionCode();

        var request = new HttpRequestMessage(_method, BuildUri(_baseAddress));
        _headers.ForEach(h => request.Headers.TryAddWithoutValidation(h.Key, h.Value));
        request.Content = BuildContent();
        if (_identifier is { } identifier)
        {
            request.Options.Set(HeappeIdentifierKey, identifier.Value);
        }

        return request;
    }

    private Uri BuildUri(Uri baseAddress)
    {
        var baseUri = string.IsNullOrEmpty(_relativePath)
            ? baseAddress
            : new Uri(baseAddress, _relativePath);

        return AddQueryParameters(baseUri, _queryParameters);
    }

    // A session code travels in the query string of a GET and in the body of any other method.
    private void AddSessionCode()
    {
        if (string.IsNullOrEmpty(_sessionCode))
        {
            return;
        }

        var target = _method == HttpMethod.Get ? _queryParameters : _bodyContentFields;
        target.Add(new KeyValuePair<string, string>("SessionCode", _sessionCode));
    }

    private HttpContent? BuildContent()
    {
        if (_bodyContentFields.Count > 0)
        {
            var json = JsonSerializer.Serialize(_bodyContentFields.ToDictionary(), DefaultJsonOptions);
            _bodyContent = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return _bodyContent;
    }

    private static Uri AddQueryParameters(Uri baseUri, List<KeyValuePair<string, string>> queryParameters)
    {
        if (queryParameters == null || queryParameters.Count == 0)
        {
            return baseUri;
        }

        var ub = new UriBuilder(baseUri)
        {
            Query = QueryHelpers.AddQueryString("", queryParameters).TrimStart('?')
        };
        return ub.Uri;
    }
    /// <summary>
    /// Disposes resources managed by this builder, including any <see cref="HttpContent"/> created by the builder.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="Dispose()"/>; false if called from a finalizer.</param>
    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _bodyContent?.Dispose();
            }

            _disposed = true;
        }
    }

    private static void AddQueryParametersWithValidation(List<KeyValuePair<string, string>> targetList, IEnumerable<KeyValuePair<string, string?>> sourceList)
    {
        foreach (var param in sourceList.Where(w => !string.IsNullOrWhiteSpace(w.Key)))
        {
            targetList.Add(new KeyValuePair<string, string>(param.Key, param.Value ?? ""));
        }
    }
}