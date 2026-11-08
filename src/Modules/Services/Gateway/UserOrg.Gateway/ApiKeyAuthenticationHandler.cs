using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

using UserOrg.Shared;

namespace UserOrg.Gateway;

internal sealed partial class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    IHashiCorpVaultClient vaultClient,
    IOptions<Configuration.ApiKeyAuthenticationOptions> authOptions,
    HybridCache cache,
    UrlEncoder encoder,
    IAuditLogger<ApiKeyAuthenticationHandler> auditLogger) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string AnonymousActorId = "anonymous";
    private const string AnonymousActorType = "anonymous";
    private const string ServiceActorType = "service";
    private const string AuthorizationHeader = "Authorization";
    private const string VaultKeysCacheKey = "vaultServiceAccessKeys";
    private const int MaskedHashLength = 8;

    /// <summary>The legacy API key headers, in the order they are tried, with the key type each carries.</summary>
    private static readonly (string Header, EApiKeyType KeyType)[] LegacyApiKeyHeaders =
    [
        ("X-Api-ServiceToken", EApiKeyType.LegacyService),
        ("X-Api-ProviderServiceKey", EApiKeyType.LegacyProvider),
        ("X-Api-Token", EApiKeyType.LegacyService), // DDI
    ];

    private static readonly (EApiKeyType KeyType, string? key, string? KeyHash, string HeaderSource) NoApiKey =
        (EApiKeyType.Unknown, null, null, "None");

    private static readonly ActivitySource ActivitySource = new("UserOrg.Gateway.ApiKeyAuth", "1.0.0");
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ctDelay = TimeSpan.FromSeconds(authOptions.Value.TimeoutSeconds);

        using var cts = new CancellationTokenSource(ctDelay);

        var (keyType, apiKey, keyHash, headerSource) = ExtractApiKey();

        Activity.Current?.SetTag("auth.header_source", headerSource);
        Activity.Current?.SetTag("auth.key_type", keyType.ToString());

        if (keyHash is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (string.IsNullOrWhiteSpace(keyHash))
        {
            return await DenyAsync(keyType, keyHash, GatewayAuditActions.DenialReasons.ApiKeyMissing, "Missing API key.");
        }

        // returns IdentityProviderId or ProviderId as sub claim
        IServiceKeyInfo serviceKeyData;
        try
        {
            serviceKeyData = await RetriveKeyName(apiKey, keyType, cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "API key vault lookup timed out after {TimeoutSeconds}s for keyType {KeyType}", ctDelay.TotalSeconds, keyType);
            return await DenyAsync(keyType, keyHash, GatewayAuditActions.DenialReasons.ApiKeyLookupTimedOut, "API key validation timed out.");
        }

        if (string.IsNullOrEmpty(serviceKeyData?.ServiceName) || string.IsNullOrEmpty(keyHash))
        {
            return await DenyAsync(keyType, keyHash, GatewayAuditActions.DenialReasons.ApiKeyInvalid, "Invalid API key.");
        }

        var sub = ResolveSubject(serviceKeyData);
        var ticket = CreateTicket(serviceKeyData, sub, keyType);

        if (serviceKeyData.Key != apiKey)
        {
            await auditLogger.AuditBestEffortAsync(
                AuditEvent.Success(GatewayAuditActions.ApiKeyAuthenticated, sub, ServiceActorType) with { Data = KeyAuditData(keyType, keyHash) },
                _logger);
        }

        return AuthenticateResult.Success(ticket);
    }

    private static string ResolveSubject(IServiceKeyInfo serviceKeyData) => serviceKeyData switch
    {
        ProviderServiceKeyInfo providerKey => providerKey.ProviderId,
        DDIServiceKeyInfo ddiServiceKey => ddiServiceKey.ServiceName,
        _ => serviceKeyData.ServiceName!
    };

    private AuthenticationTicket CreateTicket(IServiceKeyInfo serviceKeyData, string sub, EApiKeyType keyType)
    {
        List<Claim> claims =
        [
            new (JwtRegisteredClaimNames.Sub, sub), //keyHash?
            new (JwtRegisteredClaimNames.PreferredUsername,serviceKeyData.ServiceName!),
            new (ClaimTypes.Role,keyType.ToString()),
            new (EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey)),
            new (AuthConstants.ServiceTypeClaimType, serviceKeyData.ServiceType.ToString())
        ];

        if (!string.IsNullOrEmpty(serviceKeyData.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, serviceKeyData.Email));
        }

        if (serviceKeyData is DDIServiceKeyInfo or ServiceKeyInfo)
        {
            claims.Add(new Claim(AuthConstants.IdentityTypeClaimType, "service"));
        }

        if (serviceKeyData is ProviderServiceKeyInfo providerInfo)
        {
            claims.Add(new Claim(AuthConstants.IdentityTypeClaimType, "provider"));
            claims.Add(new Claim(AuthConstants.ProviderIdClaimType, providerInfo.ProviderId));
        }

        if (serviceKeyData is ServiceKeyInfo { CustomData: not null } keyInfo)
        {
            claims.Add(new Claim(AuthConstants.CustomDataTypeClaimType, keyInfo.CustomData));
        }

        return new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name);
    }

    private async Task<AuthenticateResult> DenyAsync(EApiKeyType keyType, string keyHash, string reason, string failureMessage)
    {
        await auditLogger.AuditBestEffortAsync(
            AuditEvent.Denied(GatewayAuditActions.ApiKeyAuthenticated, AnonymousActorId, AnonymousActorType) with
            {
                Reason = reason,
                Data = KeyAuditData(keyType, keyHash),
            },
            _logger);
        return AuthenticateResult.Fail(failureMessage);
    }

    private static Dictionary<string, object?> KeyAuditData(EApiKeyType keyType, string keyHash) => new()
    {
        ["key_type"] = keyType.ToString(),
        ["key_hash"] = keyHash,
    };

    private (EApiKeyType KeyType, string? key, string? KeyHash, string HeaderSource) ExtractApiKey()
    {
        // Try modern Authorization header first
        var authHeader = Request.Headers[AuthorizationHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            return FromAuthorizationHeader(authHeader);
        }

        // Fallback to the legacy headers, in order
        foreach (var (header, keyType) in LegacyApiKeyHeaders)
        {
            var legacyValue = Request.Headers[header].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(legacyValue))
            {
                return (keyType, legacyValue, ComputeSha256Base64(legacyValue), header);
            }
        }

        return NoApiKey;
    }

    /// <summary>
    /// An API key from a non-empty Authorization header; an unknown scheme is no key, with no fallback
    /// to the legacy headers.
    /// </summary>
    private static (EApiKeyType KeyType, string? key, string? KeyHash, string HeaderSource) FromAuthorizationHeader(string authHeader)
    {
        var (keyType, keyValue) = ParseAuthorizationHeader(authHeader);

        if (keyType is EApiKeyType.Unknown)
        {
            return NoApiKey;
        }

        var keyHash = keyValue is not null
            ? ComputeSha256Base64(keyValue)
            : null;

        return (keyType, keyValue, keyHash, AuthorizationHeader);
    }

    private static (EApiKeyType KeyType, string? KeyValue) ParseAuthorizationHeader(string headerValue)
    {
        var headerSpan = headerValue.AsSpan();
        var spaceIndex = headerSpan.IndexOf(' ');

        if (spaceIndex <= 0)
        {
            return (EApiKeyType.Unknown, headerValue);
        }

        var prefix = headerSpan[..spaceIndex];
        var value = headerSpan[(spaceIndex + 1)..];

        if (!TryParseKeyTypePrefix(prefix, out var keyType))
        {
            return (EApiKeyType.Unknown, headerValue);
        }

        var keyValue = value.ToString();
        return (keyType, keyValue);
    }

    private static bool TryParseKeyTypePrefix(ReadOnlySpan<char> prefix, out EApiKeyType keyType)
    {
        if (prefix.Equals(nameof(EApiKeyType.Service), StringComparison.OrdinalIgnoreCase))
        {
            keyType = EApiKeyType.Service;
            return true;
        }

        if (prefix.Equals(nameof(EApiKeyType.Provider), StringComparison.OrdinalIgnoreCase))
        {
            keyType = EApiKeyType.Provider;
            return true;
        }

        keyType = EApiKeyType.Unknown;
        return false;
    }

    private static string ComputeSha256Base64(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        Span<byte> hashBytes = stackalloc byte[32]; // SHA256 always produces 32 bytes
        SHA256.HashData(inputBytes, hashBytes);

        Span<char> base64Chars = stackalloc char[44]; // Base64 of 32 bytes = 44 chars
        Convert.TryToBase64Chars(hashBytes, base64Chars, out var charsWritten);

        // Replace Base64 chars with Base64Url chars
        for (var i = 0; i < charsWritten; i++)
        {
            base64Chars[i] = base64Chars[i] switch
            {
                '+' => '-',
                '/' => '_',
                _ => base64Chars[i]
            };
        }

        // Trim padding '=' characters
        var endIndex = charsWritten;
        while (endIndex > 0 && base64Chars[endIndex - 1] == '=')
        {
            endIndex--;
        }

        return new string(base64Chars[..endIndex]);
    }

    private async Task<IServiceKeyInfo> RetriveKeyName(string? apiKey, EApiKeyType keyType, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("vault.lookup", ActivityKind.Internal);

        if (apiKey is null)
        {
            _logger.LogDebug("RetriveKeyName called with null apiKey; returning no result");
            activity?.AddEvent(new ActivityEvent("api.key.vault_lookup.null_key"));
            return IServiceKeyInfo.Empty;
        }

        var maskedHash = MaskedApiKeyHash(apiKey);

        _logger.LogDebug("Starting vault lookup for apiKey hash {ApiKeyHashMasked} and keyType {KeyType}", maskedHash, keyType);
        activity?.SetTag("auth.api_key_hash_masked", maskedHash);
        activity?.SetTag("auth.key_type", keyType.ToString());
        activity?.AddEvent(new ActivityEvent(
            "vault.lookup.started",
            tags: new ActivityTagsCollection { ["cache_key"] = VaultKeysCacheKey }));

        var vaultKeys = await cache.GetOrCreateAsync(
            VaultKeysCacheKey,
            factory: ct => LoadVaultKeysAsync(activity, ct),
            cancellationToken: cancellationToken);

        var keys = vaultKeys.Select(ToServiceKeyInfo).ToList();

        activity?.AddEvent(new(
            "vault.keys.ready",
            tags: new ActivityTagsCollection { ["keys_count"] = keys.Count }));

        var foundSecret = keys.FirstOrDefault(p => p.Key == apiKey || p.BackupKey == apiKey);

        if (foundSecret is null)
        {
            _logger.LogWarning("No service access key found in Vault for apiKey hash {ApiKeyHashMasked}", maskedHash);
            activity?.AddEvent(new(
                "vault.lookup.missed",
                tags: new ActivityTagsCollection { ["reason"] = "key_not_found" }));
            activity?.SetStatus(ActivityStatusCode.Error, "API key not found in Vault");
            return IServiceKeyInfo.Empty;
        }

        LogVaultEntryFound(_logger, foundSecret.ServiceName);
        activity?.SetTag("auth.service_name", foundSecret.ServiceName);
        activity?.AddEvent(new(
            "vault.lookup.hit",
            tags: new ActivityTagsCollection
            {
                ["service_name"] = foundSecret.ServiceName,
                ["has_email"] = !string.IsNullOrEmpty(foundSecret.Email)
            }));

        return foundSecret switch
        {
            DDIServiceKeyInfo ddiSecretKey => ReturnVaultPathKey(activity, ddiSecretKey),
            ProviderServiceKeyInfo providerSecretKey => ReturnProviderKey(activity, providerSecretKey),
            ServiceKeyInfo serviceKey => ReturnDirectKey(activity, serviceKey),
            _ => ReturnUnexpectedKeyType(activity, keyType, maskedHash),
        };
    }

    /// <summary>
    /// The API key hash for logging and telemetry, masked to its first 8 characters so no secret leaks.
    /// </summary>
    private string MaskedApiKeyHash(string apiKey)
    {
        string apiKeyHash;
        try
        {
            apiKeyHash = ComputeSha256Base64(apiKey);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to compute apiKey hash for telemetry");
            apiKeyHash = "<hash-error>";
        }

        return apiKeyHash.Length > MaskedHashLength ? $"{apiKeyHash[..MaskedHashLength]}..." : apiKeyHash;
    }

    /// <summary>
    /// Loads the service access keys from Vault on a cache miss; a failed lookup yields no keys and marks
    /// the activity as failed.
    /// </summary>
    private async ValueTask<IReadOnlyCollection<ServiceAccessKeys>> LoadVaultKeysAsync(Activity? activity, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cache miss for 'vaultServiceAccessKeys' - loading from Vault");
        activity?.AddEvent(new(
            "vault.cache.miss",
            tags: new ActivityTagsCollection { ["operation"] = "GetServiceAccessKeysAsync" }));
        var vaultSw = Stopwatch.StartNew();
        try
        {
            var accessKeys = await vaultClient.GetServiceAccessKeysAsync(cancellationToken);
            vaultSw.Stop();
            _logger.LogDebug("Vault GetServiceAccessKeysAsync completed in {ElapsedMs}ms", vaultSw.ElapsedMilliseconds);

            if (!accessKeys.IsSuccess)
            {
                _logger.LogError("Failed to retrieve service access keys from Vault: {ErrorMessage}", accessKeys.Error);
                activity?.AddEvent(new(
                    "vault.lookup.error",
                    tags: new ActivityTagsCollection { ["error_message"] = accessKeys.Error.Description }));
                activity?.SetStatus(ActivityStatusCode.Error, "Failed to retrieve keys from Vault");
                return [];
            }

            return accessKeys.Value;
        }
        catch (OperationCanceledException)
        {
            vaultSw.Stop();
            _logger.LogWarning("Vault GetServiceAccessKeysAsync was canceled after {ElapsedMs}ms", vaultSw.ElapsedMilliseconds);
            throw;
        }
    }

    private static IServiceKeyInfo ToServiceKeyInfo(ServiceAccessKeys k) => k.ServiceType switch
    {
        EServiceType.DDI => new DDIServiceKeyInfo(k.Name, k.ContactEmail, ServiceAccessKeys.VaultPath) { Key = k.Key, BackupKey = k.BackupKey },
        EServiceType.Provider => new ProviderServiceKeyInfo(k.Name, k.ContactEmail, k.Data ?? throw new InvalidOperationException("Provider data is missing")) { Key = k.Key, BackupKey = k.BackupKey },
        _ => new ServiceKeyInfo(k.Name, k.ContactEmail, k.Data) { Key = k.Key, BackupKey = k.BackupKey }
    };

    private DDIServiceKeyInfo ReturnVaultPathKey(Activity? activity, DDIServiceKeyInfo key)
    {
        _logger.LogDebug("Returning vault path reference for service key of {ServiceName}", key.ServiceName);
        return CompleteLookup(activity, key, "vault.result.vault_path", "vault_path", key.VaultPath);
    }

    private ProviderServiceKeyInfo ReturnProviderKey(Activity? activity, ProviderServiceKeyInfo key)
    {
        _logger.LogDebug("Returning provider data reference for provider key of {ServiceName}", key.ServiceName);
        return CompleteLookup(activity, key, "vault.result.provider_data", "providerName", !string.IsNullOrEmpty(key.ServiceName));
    }

    private ServiceKeyInfo ReturnDirectKey(Activity? activity, ServiceKeyInfo key)
    {
        _logger.LogDebug("Returning direct service key for service {ServiceName}", key.ServiceName);
        return CompleteLookup(activity, key, "vault.result.direct_key", "serviceName", !string.IsNullOrEmpty(key.ServiceName));
    }

    /// <summary>Records the lookup result event, marks the activity successful and returns the key.</summary>
    private static TKey CompleteLookup<TKey>(Activity? activity, TKey key, string eventName, string tagName, object tagValue)
        where TKey : IServiceKeyInfo
    {
        activity?.AddEvent(new ActivityEvent(
            eventName,
            tags: new ActivityTagsCollection { [tagName] = tagValue }));
        activity?.SetStatus(ActivityStatusCode.Ok);
        return key;
    }

    private IServiceKeyInfo ReturnUnexpectedKeyType(Activity? activity, EApiKeyType keyType, string maskedHash)
    {
        _logger.LogWarning("Unexpected keyType {KeyType} when retrieving key name for apiKey hash {ApiKeyHashMasked}", keyType, maskedHash);
        activity?.AddEvent(new ActivityEvent(
            "vault.lookup.unexpected_keytype",
            tags: new ActivityTagsCollection { ["key_type"] = keyType.ToString() }));
        activity?.SetStatus(ActivityStatusCode.Error, "Unexpected key type");
        return IServiceKeyInfo.Empty;
    }

    private interface IServiceKeyInfo
    {
        string Key { get; }
        string BackupKey { get; }
        string? ServiceName { get; }
        string? Email { get; }
        EServiceType ServiceType { get; }
        static IServiceKeyInfo Empty => new ServiceKeyInfo(string.Empty, string.Empty, null)
        {
            BackupKey = string.Empty,
            Key = string.Empty,
            ServiceType = EServiceType.Unknown
        };
    }

    private sealed record ProviderServiceKeyInfo(string ServiceName, string Email, string ProviderId) : IServiceKeyInfo
    {
        public required string Key { get; init; }
        public required string BackupKey { get; init; }
        public EServiceType ServiceType => EServiceType.Provider;
    }
    private sealed record ServiceKeyInfo(string ServiceName, string Email, string? CustomData) : IServiceKeyInfo
    {
        public required string Key { get; init; }
        public required string BackupKey { get; init; }
        public EServiceType ServiceType { get; init; } = EServiceType.Service;
    }
    private sealed record DDIServiceKeyInfo(string ServiceName, string Email, string VaultPath) : IServiceKeyInfo
    {
        public required string Key { get; init; }
        public required string BackupKey { get; init; }
        public EServiceType ServiceType => EServiceType.DDI;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Found vault entry for service {ServiceName}")]
    private static partial void LogVaultEntryFound(ILogger logger, string? serviceName);
}