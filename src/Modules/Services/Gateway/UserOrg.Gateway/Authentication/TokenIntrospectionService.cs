using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;

namespace UserOrg.Gateway.Authentication;

/// <summary>
/// Implementation of OAuth 2.0 token introspection service (RFC 7662).
/// Calls the introspection endpoint and caches results based on token hash and expiration.
/// </summary>
internal sealed partial class TokenIntrospectionService : ITokenIntrospectionService
{
    private const string IntrospectionHttpClientName = "IntrospectionBackChannel";
    private const string CacheKeyPrefix = "introspect:";
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenIntrospectionService> _logger;
    private readonly string? _introspectionEndpoint;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly bool _enabled;
    private readonly bool _isAnonymous;

    public TokenIntrospectionService(
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<TokenIntrospectionService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;

        // Check if introspection is enabled (default: true if not specified)
        var enabledStr = configuration["Idp:Introspection:Enabled"] ?? Environment.GetEnvironmentVariable("Idp__Introspection__Enabled");
        _enabled = string.IsNullOrWhiteSpace(enabledStr) || bool.Parse(enabledStr);

        // Load introspection configuration from environment variables (env-only per requirement)
        _clientId = configuration["Idp:Introspection:ClientId"] ?? Environment.GetEnvironmentVariable("Idp__Introspection__ClientId");
        _clientSecret = configuration["Idp:Introspection:ClientSecret"] ?? Environment.GetEnvironmentVariable("Idp__Introspection__ClientSecret");
        _introspectionEndpoint = configuration["Idp:Introspection:Endpoint"] ?? Environment.GetEnvironmentVariable("Idp__Introspection__Endpoint");

        // Determine if anonymous mode (no client credentials provided)
        _isAnonymous = string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret);

        if (!_enabled)
        {
            LogIntrospectionDisabled(_logger, environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Warning);
        }
        else
        {
            // If endpoint not explicitly configured, we'll try to discover it from Authority/.well-known
            if (string.IsNullOrWhiteSpace(_introspectionEndpoint))
            {
                var authority = configuration["Idp:Authority"] ?? Environment.GetEnvironmentVariable("Idp__Authority");
                if (!string.IsNullOrWhiteSpace(authority))
                {
                    // Discovery happens lazily on first introspection call
                    _logger.LogInformation("[INTROSPECTION] Introspection endpoint will be discovered from {Authority}", authority);
                }
            }

            if (_isAnonymous)
            {
                _logger.LogInformation("[INTROSPECTION] Using ANONYMOUS mode (no client credentials configured)");
            }
            else
            {
                _logger.LogInformation("[INTROSPECTION] Using BASIC AUTH mode (client credentials configured)");
            }
        }
    }

    private string IntrospectionMode => _isAnonymous ? "anonymous" : "basic auth";

    public async Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        // If introspection is disabled, return active immediately (bypass)
        if (!_enabled)
        {
            return new IntrospectionResult(true, null, new Dictionary<string, string[]>());
        }

        // Generate cache key from token hash (never store raw token in cache key)
        var cacheKey = CacheKeyPrefix + ComputeSha256Hash(token);

        var cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedJson))
        {
            LogCacheHit(_logger);
            return ParseJson(cachedJson);
        }

        LogCacheMiss(_logger, IntrospectionMode);
        return await IntrospectRemotelyAsync(token, cacheKey, cancellationToken);
    }

    private async Task<IntrospectionResult> IntrospectRemotelyAsync(string token, string cacheKey, CancellationToken cancellationToken)
    {
        var endpoint = await ResolveIntrospectionEndpointAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogError("[INTROSPECTION] Introspection endpoint not configured and could not be discovered");
            return Inactive();
        }

        try
        {
            return await CallAndCacheAsync(endpoint, token, cacheKey, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[INTROSPECTION] HTTP request failed when calling introspection endpoint");
            // Fail-closed: return inactive on error
            return Inactive();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INTROSPECTION] Unexpected error during introspection");
            // Fail-closed: return inactive on error
            return Inactive();
        }
    }

    private async Task<IntrospectionResult> CallAndCacheAsync(string endpoint, string token, string cacheKey, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(IntrospectionHttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("token", token)])
        };
        AddClientCredentials(request);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = ParseJson(responseJson);

        var ttl = CalculateCacheTtl(result.Exp);
        await _cache.SetStringAsync(
            cacheKey,
            responseJson,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            cancellationToken);

        LogIntrospectionSucceeded(_logger, result.Active, ttl.TotalSeconds);

        return result;
    }

    private void AddClientCredentials(HttpRequestMessage request)
    {
        if (_isAnonymous)
        {
            return;
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    private static IntrospectionResult ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseIntrospectionResponse(doc.RootElement);
    }

    private static IntrospectionResult Inactive() => new(false, null, new Dictionary<string, string[]>());

    private async Task<string?> ResolveIntrospectionEndpointAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_introspectionEndpoint))
        {
            return _introspectionEndpoint;
        }

        var authority = _configuration["Idp:Authority"] ?? Environment.GetEnvironmentVariable("Idp__Authority");
        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        try
        {
            var discoveryUrl = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
            var client = _httpClientFactory.CreateClient(IntrospectionHttpClientName);

            using var response = await client.GetAsync(discoveryUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("introspection_endpoint", out var endpoint))
            {
                var discoveredEndpoint = endpoint.GetString();
                _logger.LogInformation("[INTROSPECTION] Discovered introspection endpoint: {Endpoint}", discoveredEndpoint);
                return discoveredEndpoint;
            }

            _logger.LogWarning("[INTROSPECTION] Discovery document does not contain introspection_endpoint");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INTROSPECTION] Failed to discover introspection endpoint from {Authority}", authority);
            return null;
        }
    }

    private static IntrospectionResult ParseIntrospectionResponse(JsonElement root)
    {
        var active = root.TryGetProperty("active", out var activeProp) && activeProp.GetBoolean();

        var claims = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            AddClaim(claims, prop);
        }

        return new IntrospectionResult(active, ReadExpiry(root), claims);
    }

    private static DateTimeOffset? ReadExpiry(JsonElement root)
        => root.TryGetProperty("exp", out var expProp) && expProp.ValueKind == JsonValueKind.Number && expProp.TryGetInt64(out var expValue)
            ? DateTimeOffset.FromUnixTimeSeconds(expValue)
            : null;

    /// <summary>
    /// Adds a response member other than <c>active</c> and <c>exp</c> as a claim when it holds at least one
    /// non-empty string (a string, or an array whose non-empty string items are kept).
    /// </summary>
    private static void AddClaim(Dictionary<string, string[]> claims, JsonProperty prop)
    {
        if (prop.NameEquals("active") || prop.NameEquals("exp"))
        {
            return;
        }

        var values = ClaimValues(prop.Value);
        if (values.Length > 0)
        {
            claims[prop.Name] = values;
        }
    }

    private static string[] ClaimValues(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => NonEmptyStrings([value]),
        JsonValueKind.Array => NonEmptyStrings(value.EnumerateArray()),
        _ => [],
    };

    private static string[] NonEmptyStrings(IEnumerable<JsonElement> items) => items
        .Where(x => x.ValueKind == JsonValueKind.String)
        .Select(x => x.GetString() ?? string.Empty)
        .Where(x => !string.IsNullOrEmpty(x))
        .ToArray();

    private static TimeSpan CalculateCacheTtl(DateTimeOffset? exp)
    {
        if (!exp.HasValue)
        {
            return DefaultCacheTtl;
        }

        var diff = exp.Value - DateTimeOffset.UtcNow;
        if (diff <= TimeSpan.FromSeconds(5))
        {
            return TimeSpan.FromSeconds(5); // Minimum TTL
        }

        return diff < MaxCacheTtl ? diff : MaxCacheTtl;
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [LoggerMessage(Message = "[INTROSPECTION] Token introspection is DISABLED via configuration")]
    private static partial void LogIntrospectionDisabled(ILogger logger, LogLevel level);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[INTROSPECTION] Cache hit for token")]
    private static partial void LogCacheHit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[INTROSPECTION] Cache miss, calling introspection endpoint ({Mode})")]
    private static partial void LogCacheMiss(ILogger logger, string mode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[INTROSPECTION] Token introspection successful, active={Active}, cached for {Ttl}s")]
    private static partial void LogIntrospectionSucceeded(ILogger logger, bool active, double ttl);
}