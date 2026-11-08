using System.Diagnostics;
using System.Text;
using System.Text.Json;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HashiCorpVault;

internal sealed class HashiCorpVaultClient(
    IHttpClientFactory httpClientFactory,
    IOptions<VaultOptions> options,
    ILogger<HashiCorpVaultClient> logger) : IHashiCorpVaultClient
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly VaultOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<HashiCorpVaultClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<Result<IReadOnlyCollection<ServiceAccessKeys>, Error>> GetServiceAccessKeysAsync(CancellationToken cancellation = default)
        => GetAsync<ServiceAccessKeys>(ServiceAccessKeys.VaultPath, cancellation);


    public async Task<Result<IReadOnlyCollection<DataStagingLocationKey>, Error>> GetDDIDataStagingKeysAsync(CancellationToken cancellation = default)
    {
        var all = await GetServiceAccessKeysAsync(cancellation);
        if (!all.IsSuccess)
        {
            return Result<IReadOnlyCollection<DataStagingLocationKey>, Error>.Failure(all.Error);
        }

        var list = all.Value
            .Where(x => x.ServiceType == UserOrg.Shared.EServiceType.DDI)
            .Select(x => new DataStagingLocationKey
            {
                Name = x.Name,
                Key = x.Key,
                BackupKey = x.BackupKey,
                JsonData = x.Data ?? string.Empty
            })
            .ToList();

        return Result<IReadOnlyCollection<DataStagingLocationKey>, Error>.Success(list);
    }
    public async Task<Result<string, Error>> GetServiceAccessKeyDataAsync(string KeyName, CancellationToken cancellation = default)
    {

        var keys = await GetAsync<ServiceAccessKeys>(ServiceAccessKeys.VaultPath, cancellation);

        return keys.FindByName(KeyName, x => x.Name)
            .Match(
            succ => Result<string, Error>.Success(succ.Data ?? ""),
            fail => Result<string, Error>.Failure(fail)
        );
    }
    public async Task<Result<ExternalServiceAccessCredentials, Error>> GetExternalCredentials(string Name, CancellationToken cancellation)
    {
        var data = await GetAsync<ExternalServiceAccessCredentials>(ExternalServiceAccessCredentials.VaultPath, cancellation);
        if (!data.IsSuccess)
        {
            return Result<ExternalServiceAccessCredentials, Error>.Failure(
                new Error(
                    "Vault.FetchFailed",
                    ErrorSeverity.Error,
                    $"Failed to fetch external credentials '{Name}' from Vault path '{ExternalServiceAccessCredentials.VaultPath}'."
                )
            );
        }

        return data.FindByName(Name, c => c.Name)
            .Match(
            succ => Result<ExternalServiceAccessCredentials, Error>.Success(succ),
            fail => Result<ExternalServiceAccessCredentials, Error>.Failure(fail)
        );
    }

    public async Task<Result<BulkUpdateResult, Error>> UpdateServiceAccessKeysBulkAsync(IEnumerable<DataStagingLocationKey> inputItems, CancellationToken cancellation = default)
    {
        using var activity = VaultTelemetry.ActivitySource.StartActivity("Vault.BulkUpdateServiceAccessKeys", ActivityKind.Client);
        activity?.SetTag("vault.mount", _options.MountPoint);
        activity?.SetTag("vault.path", ServiceAccessKeys.VaultPath);
        activity?.SetTag("http.method", "POST");

        try
        {
            var client = _httpClientFactory.CreateClient("Vault");

            // fetch existing entries (if fetch fails, treat as empty set)
            var existingRes = await GetAsync<ServiceAccessKeys>(ServiceAccessKeys.VaultPath, cancellation);
            var existing = existingRes.IsSuccess ? existingRes.Value : new List<ServiceAccessKeys>();

            var existingByName = existing.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
            var incomingList = (inputItems ?? Array.Empty<DataStagingLocationKey>()).Where(i => !string.IsNullOrEmpty(i.Name)).ToList();
            var incomingByName = incomingList.ToDictionary(i => i.Name!, StringComparer.OrdinalIgnoreCase);

            int created = 0, updated = 0, deleted = 0;
            foreach (var name in incomingByName.Keys)
            {
                if (!existingByName.TryGetValue(name, out var ex) || ex.ServiceType != UserOrg.Shared.EServiceType.DDI)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }
            foreach (var ex in existing)
            {
                if (ex.ServiceType == UserOrg.Shared.EServiceType.DDI && !incomingByName.ContainsKey(ex.Name))
                {
                    deleted++;
                }
            }

            // Build final map: keep non-DDI existing entries, add/overwrite incoming DDI entries
            var finalMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var ex in existing)
            {
                if (ex.ServiceType != UserOrg.Shared.EServiceType.DDI)
                {
                    finalMap[ex.Name] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(ex));
                }
            }

            foreach (var incoming in incomingList)
            {
                var entry = new ServiceAccessKeys
                {
                    Name = incoming.Name ?? string.Empty,
                    Key = incoming.Key ?? string.Empty,
                    BackupKey = incoming.BackupKey ?? string.Empty,
                    ServiceType = UserOrg.Shared.EServiceType.DDI,
                    Data = incoming.JsonData ?? string.Empty,
                    ContactEmail = string.Empty
                };

                finalMap[entry.Name] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(entry));
            }

            var payload = new { data = finalMap };
            var relative = _options.BuildVaultDataPath(ServiceAccessKeys.VaultPath);

            using var req = new HttpRequestMessage(HttpMethod.Post, relative)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellation);
            activity?.SetTag("http.status_code", (int)resp.StatusCode);

            if (!resp.IsSuccessStatusCode)
            {
                return Result<BulkUpdateResult, Error>.Failure(new Error("Vault.WriteFailed", ErrorSeverity.Error, $"Failed to write ServiceAccessKeys to Vault. StatusCode={(int)resp.StatusCode}"));
            }

            return Result<BulkUpdateResult, Error>.Success(new BulkUpdateResult(created, updated, deleted));
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "operation canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error updating ServiceAccessKeys in Vault path {Path}", ServiceAccessKeys.VaultPath);
            throw;
        }
    }

    private async Task<Result<IReadOnlyCollection<T>, Error>> GetAsync<T>(string vaultPath, CancellationToken cancellation) where T : notnull, IVaultModel
    {
        using var activity = VaultTelemetry.ActivitySource.StartActivity("Vault.FetchData", ActivityKind.Client);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("vault.mount", _options.MountPoint);

            activity.SetTag("vault.path", vaultPath);
            activity.SetTag("http.method", "GET");
        }

        var client = _httpClientFactory.CreateClient("Vault");

        var relative = _options.BuildVaultDataPath(vaultPath);
        activity?.SetTag("http.method", "GET");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, relative);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellation);

            activity?.SetTag("http.status_code", (int)resp.StatusCode);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (activity?.IsAllDataRequested == true)
                {
                    activity.SetTag("vault.item_count", 0);
                }

                return Result<IReadOnlyCollection<T>, Error>.Failure(new Error("", ErrorSeverity.Error, ""));
            }

            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellation);

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);

            if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("data", out var inner))
            {
                activity?.AddEvent(new ActivityEvent("vault.parse_error"));
                _logger.LogError("Vault response missing 'data.data' structure for path {Path}", vaultPath);
                return Result<IReadOnlyCollection<T>, Error>.Failure(new Error("", ErrorSeverity.Error, ""));

            }

            var list = new List<T>();
            foreach (var prop in inner.EnumerateObject())
            {
                try
                {
                    T? item = default;
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        item = JsonSerializer.Deserialize<T>(prop.Value.GetRawText());
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var raw = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(raw))
                        {
                            item = JsonSerializer.Deserialize<T>(raw);
                        }
                    }
                    if (item is not null)
                    {
                        list.Add(item);
                    }
                }
                catch (JsonException jex)
                {
                    activity?.AddEvent(new ActivityEvent("vault.parse_error"));
                    _logger.LogError(jex, "Failed to deserialize vault item {Key} into {Type}", prop.Name, typeof(T).FullName);
                }
            }
            if (activity?.IsAllDataRequested == true)
            {
                activity.SetTag("vault.item_count", list.Count);
            }
            return Result<IReadOnlyCollection<T>, Error>.Success(list);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "operation canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error fetching vault data for path {Path}", vaultPath);
            throw;
        }
    }

}