using System.Text.Json;
using System.Text.Json.Serialization;

using HashiCorpVault.Contracts.Models;

using Microsoft.Extensions.Configuration;

namespace HashiCorpVault.Contracts.Configuration;

/// <summary>
/// Loads secrets from HashiCorp Vault KV-v2 and injects them into the
/// <see cref="IConfiguration"/> tree during application startup.
/// </summary>
/// <remarks>
/// <para>
/// Executed once during host build, before the DI container is available.
/// Authentication is handled by the Vault proxy agent — no token is required from the application.
/// </para>
/// <para>
/// If Vault is unreachable or a credential is not found, a warning is written to
/// <see cref="Console.Error"/> and startup continues. The affected configuration
/// key is simply left unset.
/// </para>
/// </remarks>
internal sealed class VaultConfigurationProvider(VaultConfigurationSource source) : ConfigurationProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly VaultConfigurationSource _source = source;

    /// <inheritdoc/>
    public override void Load()
    {
        if (_source.Mappings.Count == 0)
        {
            return;
        }

        try
        {
            var credentials = FetchCredentials();

            foreach (var mapping in _source.Mappings)
            {
                if (!credentials.TryGetValue(mapping.CredentialName, out var cred))
                {
                    Console.Error.WriteLine(
                        $"[VaultConfigurationProvider] Credential '{mapping.CredentialName}' not found in Vault path '{ExternalServiceAccessCredentials.VaultPath}'.");
                    continue;
                }

                var value = mapping.FieldSelector(cred);
                if (value is not null)
                {
                    Data[mapping.TargetConfigKey] = value;
                }
            }
        }
#pragma warning disable CA1031 // Intentionally catches all exceptions to prevent startup failure
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine(
                $"[VaultConfigurationProvider] Failed to load secrets from Vault at '{_source.VaultAddress}': {ex.Message}");
        }
    }

    private string BuildVaultDataPath(string secretPath)
    {
        if (string.IsNullOrWhiteSpace(_source.MountPoint))
        {
            throw new InvalidOperationException(
                $"VaultOptions.MountPoint must not be null or whitespace. Actual value: '{_source.MountPoint ?? "null"}'.");
        }

        // Build base paths. If AddressPrefix is set, include it before mountpoint.
        var prefix = string.IsNullOrWhiteSpace(_source.AddressPrefix)
            ? string.Empty
            : _source.AddressPrefix!.Trim('/');
        var mount = _source.MountPoint.Trim('/');

        var dataBasePath = string.IsNullOrEmpty(prefix)
            ? $"/v1/{mount}/data"
            : $"/v1/{mount}/data/{prefix}";

        if (string.IsNullOrEmpty(secretPath))
        {
            return dataBasePath;
        }

        var normalizedSecretPath = secretPath.TrimStart('/');
        return $"{dataBasePath}/{normalizedSecretPath}";
    }

    private Dictionary<string, ExternalServiceAccessCredentials> FetchCredentials()
    {

        var url = BuildVaultDataPath(ExternalServiceAccessCredentials.VaultPath);

        using var client = new HttpClient
        {
            BaseAddress = new Uri($"{_source.VaultAddress.TrimEnd('/')}/")
        };

        using var response = client.GetAsync(new Uri(url, UriKind.Relative)).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"[VaultConfigurationProvider] Vault returned HTTP {(int)response.StatusCode} for path '{url}'.");
            return [];
        }

        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("data", out var inner))
        {
            Console.Error.WriteLine("[VaultConfigurationProvider] Vault response missing expected 'data.data' structure.");
            return [];
        }

        var result = new Dictionary<string, ExternalServiceAccessCredentials>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in inner.EnumerateObject())
        {
            var cred = TryDeserializeCredential(prop.Value);
            if (cred is not null)
            {
                result[cred.Name] = cred;
            }
        }

        return result;
    }

    private static ExternalServiceAccessCredentials? TryDeserializeCredential(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => JsonSerializer.Deserialize<ExternalServiceAccessCredentials>(
                    element.GetRawText(), _jsonOptions),
                JsonValueKind.String when element.GetString() is { Length: > 0 } raw =>
                    JsonSerializer.Deserialize<ExternalServiceAccessCredentials>(raw, _jsonOptions),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}