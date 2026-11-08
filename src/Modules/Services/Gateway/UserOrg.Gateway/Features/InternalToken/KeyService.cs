using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.IdentityModel.Tokens;

namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Service responsible for generating, rotating, and managing cryptographic keys for signing internal JWT tokens.
/// Supports multiple algorithms: RSA (2048/4096) and ECDSA (P-256/P-521).
/// </summary>
internal sealed class KeyService : IHostedService, IDisposable
{
    private const string ServiceActorId = "gateway";
    private const string ServiceActorType = "service";

    /// <summary>JWK curve (<c>crv</c>) and algorithm (<c>alg</c>) per supported ECDSA variant.</summary>
    private static readonly Dictionary<KeyAlgorithm, (string Crv, string Alg)> EcJwkCurves = new()
    {
        [KeyAlgorithm.ECDSA256] = ("P-256", "ES256"),
        [KeyAlgorithm.ECDSA512] = ("P-521", "ES512"),
    };

    private readonly ILogger<KeyService> _logger;
    private readonly InternalTokenOptions _options;
    private readonly ConcurrentDictionary<string, KeyMaterial> _keys = new();
    private Timer? _rotationTimer;
    private string _currentKeyId = string.Empty;
    private readonly TimeSpan _keyRotationInterval;
    private readonly TimeSpan _keyGracePeriod;
    private readonly HybridCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    public KeyService(ILogger<KeyService> logger, InternalTokenOptions options, HybridCache cache, IServiceScopeFactory scopeFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _keyRotationInterval = TimeSpan.FromHours(_options.KeyRotationIntervalHours);
        _keyGracePeriod = TimeSpan.FromHours(_options.KeyGracePeriodHours);

        _logger.LogInformation("KeyService initialized with algorithm: {Algorithm}", _options.KeyAlgorithm);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KeyService starting - generating initial key pair using {Algorithm}", _options.KeyAlgorithm);

        await GenerateNewKeyAsync();
        await AuditAsync(AuditEvent.Success(GatewayAuditActions.SigningKeyGenerated, ServiceActorId, ServiceActorType) with
        {
            Data = new Dictionary<string, object?> { ["kid"] = _currentKeyId },
        });

        _rotationTimer = new Timer(async _ => await RotateKeysAsync(), null, _keyRotationInterval, _keyRotationInterval);

        _logger.LogInformation("KeyService started with rotation interval: {Interval}", _keyRotationInterval);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KeyService stopping");
        _rotationTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _rotationTimer?.Dispose();
        foreach (var key in _keys.Values)
        {
            key.Dispose();
        }
        _keys.Clear();
    }

    private async Task GenerateNewKeyAsync()
    {
        var kid = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        try
        {
            switch (_options.KeyAlgorithm)
            {
                case KeyAlgorithm.RSA2048:
                await GenerateRsaKeyAsync(kid, 2048, createdAt);
                break;
                case KeyAlgorithm.RSA4096:
                await GenerateRsaKeyAsync(kid, 4096, createdAt);
                break;
                case KeyAlgorithm.ECDSA256:
                await GenerateEcdsaKeyAsync(kid, ECCurve.NamedCurves.nistP256, "P-256", SecurityAlgorithms.EcdsaSha256, createdAt);
                break;
                case KeyAlgorithm.ECDSA512:
                await GenerateEcdsaKeyAsync(kid, ECCurve.NamedCurves.nistP521, "P-521", SecurityAlgorithms.EcdsaSha512, createdAt);
                break;
                default:
                throw new InvalidOperationException($"Unsupported algorithm: {_options.KeyAlgorithm}");
            }

            _currentKeyId = kid;
            _logger.LogInformation("Generated new {Algorithm} key pair with kid: {Kid}", _options.KeyAlgorithm, kid);
            await _cache.RemoveAsync(new[] { "jwks:json" });
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Error generating new key with algorithm {Algorithm}", _options.KeyAlgorithm);
            throw;
        }
    }

    private async Task GenerateRsaKeyAsync(string kid, int keySize, DateTime createdAt)
    {
        var rsa = RSA.Create(keySize);
        var keyMaterial = new KeyMaterial
        {
            Kid = kid,
            Rsa = rsa,
            CreatedAt = createdAt,
            Algorithm = _options.KeyAlgorithm
        };

        _keys.TryAdd(kid, keyMaterial);

        var parameters = rsa.ExportParameters(true);
        var cached = new SerializableRsaParameters
        {
            Kid = kid,
            Algorithm = _options.KeyAlgorithm.ToString(),
            Modulus = parameters.Modulus,
            Exponent = parameters.Exponent,
            D = parameters.D,
            P = parameters.P,
            Q = parameters.Q,
            DP = parameters.DP,
            DQ = parameters.DQ,
            InverseQ = parameters.InverseQ
        };

        var signingOptions = new HybridCacheEntryOptions { Expiration = _keyRotationInterval + _keyGracePeriod };
        await _cache.SetAsync(GetSigningCacheKey(kid), cached, signingOptions);
    }

    private async Task GenerateEcdsaKeyAsync(string kid, ECCurve curve, string curveName, string algorithm, DateTime createdAt)
    {
        var ecdsa = ECDsa.Create(curve);
        var keyMaterial = new KeyMaterial
        {
            Kid = kid,
            Ecdsa = ecdsa,
            CreatedAt = createdAt,
            Algorithm = _options.KeyAlgorithm
        };

        _keys.TryAdd(kid, keyMaterial);

        var parameters = ecdsa.ExportParameters(true);
        var cached = new SerializableEcdsaParameters
        {
            Kid = kid,
            Algorithm = _options.KeyAlgorithm.ToString(),
            CurveName = curveName,
            SigningAlgorithm = algorithm,
            X = parameters.Q.X,
            Y = parameters.Q.Y,
            D = parameters.D
        };

        var signingOptions = new HybridCacheEntryOptions { Expiration = _keyRotationInterval + _keyGracePeriod };
        await _cache.SetAsync(GetSigningCacheKey(kid), cached, signingOptions);
    }

    private async Task RotateKeysAsync()
    {
        try
        {
            _logger.LogInformation("Starting automatic key rotation");

            await GenerateNewKeyAsync();

            var cutoffTime = DateTime.UtcNow - _keyGracePeriod;
            var expiredKeys = _keys.Where(k => k.Value.CreatedAt < cutoffTime && k.Key != _currentKeyId).ToList();
            var prunedKeyIds = new List<string>();

            foreach (var expiredKey in expiredKeys)
            {
                if (_keys.TryRemove(expiredKey.Key, out var removed))
                {
                    removed.Dispose();
                    prunedKeyIds.Add(expiredKey.Key);
                    _logger.LogInformation("Removed expired key: {Kid}", expiredKey.Key);
                    await _cache.RemoveAsync(new[] { GetSigningCacheKey(expiredKey.Key) });
                }
            }

            _logger.LogInformation("Key rotation completed. Active keys: {Count}, Current kid: {Kid}",
                _keys.Count, _currentKeyId);

            await AuditAsync(AuditEvent.Success(GatewayAuditActions.SigningKeyRotated, ServiceActorId, ServiceActorType) with
            {
                Data = new Dictionary<string, object?> { ["kid"] = _currentKeyId, ["pruned_kids"] = prunedKeyIds },
            });
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Error during key rotation");
            await AuditAsync(AuditEvent.Failure(GatewayAuditActions.SigningKeyRotated, ServiceActorId, ServiceActorType));
        }
    }

    private async Task AuditAsync(AuditEvent auditEvent)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLogger<KeyService>>().AuditBestEffortAsync(auditEvent, _logger);
    }

    internal void ManualRotate()
    {
        _logger.LogWarning("Manual key rotation triggered");
        // Fire-and-forget rotate on manual trigger to keep API surface simple
        RotateKeysAsync();
    }

    public async Task<SigningCredentials> GetSigningCredentialsAsync()
    {
        if (string.IsNullOrEmpty(_currentKeyId) || !_keys.TryGetValue(_currentKeyId, out var keyMaterial))
        {
            throw new InvalidOperationException("No active signing key available");
        }

        var cacheKey = GetSigningCacheKey(_currentKeyId);

        return keyMaterial.Algorithm switch
        {
            KeyAlgorithm.RSA2048 or KeyAlgorithm.RSA4096 => await GetRsaSigningCredentialsAsync(cacheKey, keyMaterial),
            KeyAlgorithm.ECDSA256 or KeyAlgorithm.ECDSA512 => await GetEcdsaSigningCredentialsAsync(cacheKey, keyMaterial),
            _ => throw new InvalidOperationException($"Unsupported algorithm: {keyMaterial.Algorithm}")
        };
    }

    private async Task<SigningCredentials> GetRsaSigningCredentialsAsync(string cacheKey, KeyMaterial keyMaterial)
    {
        var cachedParams = await _cache.GetOrCreateAsync<SerializableRsaParameters>(cacheKey, ct =>
        {
            var p = keyMaterial.Rsa!.ExportParameters(true);
            var c = new SerializableRsaParameters
            {
                Kid = keyMaterial.Kid,
                Algorithm = keyMaterial.Algorithm.ToString(),
                Modulus = p.Modulus,
                Exponent = p.Exponent,
                D = p.D,
                P = p.P,
                Q = p.Q,
                DP = p.DP,
                DQ = p.DQ,
                InverseQ = p.InverseQ
            };
            return new ValueTask<SerializableRsaParameters>(c);
        }, new HybridCacheEntryOptions { Expiration = _keyRotationInterval + _keyGracePeriod });

        var rsaParameters = new RSAParameters
        {
            Modulus = cachedParams.Modulus,
            Exponent = cachedParams.Exponent,
            D = cachedParams.D,
            P = cachedParams.P,
            Q = cachedParams.Q,
            DP = cachedParams.DP,
            DQ = cachedParams.DQ,
            InverseQ = cachedParams.InverseQ
        };

        var rsaForSigning = RSA.Create();
        rsaForSigning.ImportParameters(rsaParameters);

        var securityKey = new RsaSecurityKey(rsaForSigning)
        {
            KeyId = cachedParams.Kid
        };

        return new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    }

    private async Task<SigningCredentials> GetEcdsaSigningCredentialsAsync(string cacheKey, KeyMaterial keyMaterial)
    {
        var cachedParams = await _cache.GetOrCreateAsync<SerializableEcdsaParameters>(cacheKey, ct =>
        {
            var p = keyMaterial.Ecdsa!.ExportParameters(true);
            var c = new SerializableEcdsaParameters
            {
                Kid = keyMaterial.Kid,
                Algorithm = keyMaterial.Algorithm.ToString(),
                CurveName = _options.KeyAlgorithm == KeyAlgorithm.ECDSA256 ? "P-256" : "P-521",
                SigningAlgorithm = _options.KeyAlgorithm == KeyAlgorithm.ECDSA256
                    ? SecurityAlgorithms.EcdsaSha256
                    : SecurityAlgorithms.EcdsaSha512,
                X = p.Q.X,
                Y = p.Q.Y,
                D = p.D
            };
            return new ValueTask<SerializableEcdsaParameters>(c);
        }, new HybridCacheEntryOptions { Expiration = _keyRotationInterval + _keyGracePeriod });

        var curve = _options.KeyAlgorithm == KeyAlgorithm.ECDSA256
            ? ECCurve.NamedCurves.nistP256
            : ECCurve.NamedCurves.nistP521;

        var ecdsaForSigning = ECDsa.Create(curve);
        var ecdsaParameters = new ECParameters
        {
            Curve = curve,
            Q = new ECPoint { X = cachedParams.X, Y = cachedParams.Y },
            D = cachedParams.D
        };
        ecdsaForSigning.ImportParameters(ecdsaParameters);

        var securityKey = new ECDsaSecurityKey(ecdsaForSigning)
        {
            KeyId = cachedParams.Kid
        };

        return new SigningCredentials(securityKey, cachedParams.SigningAlgorithm);
    }

    internal string GetCurrentKeyId() => _currentKeyId;

    internal JsonWebKeySet GetJsonWebKeySet()
    {
        var jwks = new JsonWebKeySet();

        foreach (var keyPair in _keys.Values.OrderByDescending(k => k.CreatedAt))
        {
            if (ToJsonWebKey(keyPair) is { } jwk)
            {
                jwks.Keys.Add(jwk);
            }
        }

        return jwks;
    }

    /// <summary>The public JWK of a key pair: RSA when it holds an RSA key, else EC; null when it holds neither.</summary>
    private static JsonWebKey? ToJsonWebKey(KeyMaterial keyPair)
    {
        if (keyPair.Rsa != null)
        {
            return ToRsaJsonWebKey(keyPair.Kid, keyPair.Rsa);
        }

        return keyPair.Ecdsa != null ? ToEcJsonWebKey(keyPair.Kid, keyPair.Algorithm, keyPair.Ecdsa) : null;
    }

    private static JsonWebKey ToRsaJsonWebKey(string kid, RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Kid = kid,
            Alg = "RS256",
            N = Base64UrlEncoder.Encode(parameters.Modulus!),
            E = Base64UrlEncoder.Encode(parameters.Exponent!)
        };
    }

    private static JsonWebKey ToEcJsonWebKey(string kid, KeyAlgorithm algorithm, ECDsa ecdsa)
    {
        var parameters = ecdsa.ExportParameters(false);
        var (crv, alg) = EcJwkCurves.TryGetValue(algorithm, out var curve)
            ? curve
            : throw new InvalidOperationException($"Unsupported ECDSA variant: {algorithm}");

        return new JsonWebKey
        {
            Kty = "EC",
            Use = "sig",
            Kid = kid,
            Alg = alg,
            Crv = crv,
            X = Base64UrlEncoder.Encode(parameters.Q.X!),
            Y = Base64UrlEncoder.Encode(parameters.Q.Y!)
        };
    }

    internal async Task<string> GetCachedJwksJsonAsync()
    {
        const string cacheKey = "jwks:json";

        var json = await _cache.GetOrCreateAsync<string>(cacheKey, ct =>
        {
            var jwks = GetJsonWebKeySet();
            var j = JsonSerializer.Serialize(jwks, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new ValueTask<string>(j);
        }, new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(_options.JwksCacheExpirationSeconds) });

        return json;
    }

    private static string GetSigningCacheKey(string kid) => $"signing:{kid}";

    internal Dictionary<string, object> GetKeyMetrics()
    {
        return new Dictionary<string, object>
        {
            ["currentKeyId"] = _currentKeyId,
            ["totalKeys"] = _keys.Count,
            ["keys"] = _keys.Values.Select(k => new
            {
                kid = k.Kid,
                createdAt = k.CreatedAt,
                ageHours = (DateTime.UtcNow - k.CreatedAt).TotalHours,
                isActive = k.Kid == _currentKeyId
            }).ToList(),
            ["rotationIntervalHours"] = _keyRotationInterval.TotalHours,
            ["gracePeriodHours"] = _keyGracePeriod.TotalHours
        };
    }

    private class KeyMaterial : IDisposable
    {
        public required string Kid { get; init; }
        public RSA? Rsa { get; init; }
        public ECDsa? Ecdsa { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required KeyAlgorithm Algorithm { get; init; }

        public void Dispose()
        {
            Rsa?.Dispose();
            Ecdsa?.Dispose();
        }
    }

    private class SerializableRsaParameters
    {
        public required string Kid { get; init; }
        public string Algorithm { get; init; } = "RSA2048";
        public byte[]? Modulus { get; init; }
        public byte[]? Exponent { get; init; }
        public byte[]? D { get; init; }
        public byte[]? P { get; init; }
        public byte[]? Q { get; init; }
        public byte[]? DP { get; init; }
        public byte[]? DQ { get; init; }
        public byte[]? InverseQ { get; init; }
    }

    private class SerializableEcdsaParameters
    {
        public required string Kid { get; init; }
        public string Algorithm { get; init; } = "ECDSA256";
        public string CurveName { get; init; } = "P-256";
        public string SigningAlgorithm { get; init; } = SecurityAlgorithms.EcdsaSha256;
        public byte[]? X { get; init; }
        public byte[]? Y { get; init; }
        public byte[]? D { get; init; }
    }
}