using System.Security.Cryptography;
using System.Text;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.VersionResolver;
using Heappe.Helpers.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Caching.Hybrid;

using UserOrg.Shared;

namespace Heappe.Features.Connection;

internal sealed class HeappeConnectionService(
    IHeappeService heappeService,
    Heappe.Contracts.Connection.IHeappeConnectionFactory connectionFactory,
    IHeappeVersionResolver heappeVersionResolver,
    IHttpClientFactory httpClientFactory,
    HybridCache cache) : IHeappeConnectionService
{
    private static readonly Version _useTokenMinimalVersion = new(6, 1, 1);

    private static readonly HybridCacheEntryOptions _clustersCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5)
    };

    public async Task<HashSet<CommandTemplatePartitionResponse>> GetActiveCommandTemplatesAsync(Guid projectId, CancellationToken ct)
    {
        // IHeappeService now returns Result (main's ADR-0074 fast-fail). This wrapper's contract is a bare set,
        // so surface a failure as an exception rather than masquerading a DB error as "no partitioning".
        var result = await heappeService.GetActiveCommandTemplatesAsync(projectId, ct);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Description);
    }

    public async ValueTask<bool> IsHostAliveAsync(HeappeIdentifier identifier, CancellationToken ct)
    {
        var connection = await connectionFactory.ForPublicAsync(identifier, ct);
        if (!connection.IsSuccess)
        {
            return false;
        }

        return await ProbeAsync(new Uri(connection.Value.BaseAddress, HeappeVersionRemoteResolver.heappeHealthEndoint), ct);
    }

    private async ValueTask<bool> ProbeAsync(Uri healthUri, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HeappeIntegrationExtensions.HeappeProbeClientName);
            using var resp = await client.GetAsync(healthUri, HttpCompletionOption.ResponseHeadersRead, ct);

            // Any HTTP response from the proxy (even 404/500) means the instance is reachable — heappe/Health may be
            // absent while the API itself works. Only a transport failure or probe timeout counts as "dead".
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Probe timed out (client Timeout elapsed, not the caller's token) — treat host as dead.
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async ValueTask<Result<IList<ClusterInfoModel>, Error>> GetClusterCatalogAsync(
        string projectShortName,
        HeappeIdentifier identifier,
        string bearerToken,
        CancellationToken ct)
    {
        try
        {
            var clusters = await cache.GetOrCreateAsync(
                CatalogKey(identifier, bearerToken),
                async token =>
                {
                    var heappeVersion = await heappeVersionResolver.GetVersion(projectShortName, identifier, token);
                    var connection = await connectionFactory.ForUserAsync(identifier, bearerToken, useToken: heappeVersion >= _useTokenMinimalVersion, token);
                    if (!connection.IsSuccess)
                    {
                        throw new HeappeClustersUnavailableException(connection.Error);
                    }
                        
                    var connInfo = connection.Value;

                    Result<IList<ClusterInfoModel>, Error> response = connInfo switch
                    {
                        ServiceTokenHeappeConnectionInfo => (await heappeService.GetClusters(connInfo, token))
                            .Map<IList<ClusterDetailedModel>, IList<ClusterInfoModel>, Error>(
                                x => x.Select(ClusterInfoModel.MapFrom).ToList()),
                        _ => await heappeService.GetListAvailableClusters(
                            connInfo, [new("ForceRefresh", "true")], token)
                    };

                    // Throw on failure so HybridCache does not cache transient HEAppE errors.
                    if (!response.IsSuccess)
                        throw new HeappeClustersUnavailableException(response.Error);

                    // A catalog with no node type carrying project allocations is a mid-rebuild skeleton (same
                    // guard as the v1 adapter); cached it would serve an empty templates list for the full TTL.
                    var catalog = response.Value ?? [];
                    return catalog.Any(ClusterHasUsableData)
                        ? catalog
                        : throw new HeappeClustersUnavailableException(Errors.HeappeCatalogWithoutUsableData(identifier));
                },
                _clustersCacheOptions,
                tags: [CacheTags.HeappeClusters(identifier)],
                cancellationToken: ct);

            return Result<IList<ClusterInfoModel>, Error>.Success(clusters);
        }
        catch (HeappeClustersUnavailableException ex)
        {
            return Result<IList<ClusterInfoModel>, Error>.Failure(ex.Error);
        }
    }

    public ValueTask ForgetClusterCatalogAsync(HeappeIdentifier identifier, string bearerToken, CancellationToken ct)
        => cache.RemoveAsync(CatalogKey(identifier, bearerToken), ct);

    private static string CatalogKey(HeappeIdentifier identifier, string bearerToken)
        => $"{CacheTags.HeappeClusters(identifier)}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bearerToken)))}";

    private static bool ClusterHasUsableData(ClusterInfoModel cluster)
        => (cluster.NodeTypes ?? []).Any(nt => nt.Projects is { Count: > 0 });

    private sealed class HeappeClustersUnavailableException(Error error) : Exception
    {
        public Error Error { get; } = error;
    }
}