using Heappe.Features.Connection;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

using UserOrg.Shared;

namespace Heappe.Features.Nexus;

internal sealed class HeappeInstanceDirectory(
    NexusRegistryClient registry,
    HybridCache cache,
    IOptions<TokenExchangeOptions> options,
    TimeProvider timeProvider) : IHeappeInstanceDirectory
{
    private const string SnapshotKey = "Heappe_Nexus_Directory";

    private NexusOptions Nexus => options.Value.Nexus;

    public async ValueTask<Result<HeappeRoute, Error>> ResolveAsync(HeappeIdentifier identifier, CancellationToken ct)
    {
        try
        {
            var snapshot = await CurrentSnapshotAsync(ct);
            var route = HeappeRoute.RouteFor(
                Nexus.BaseUrl!,
                identifier,
                snapshot.Instances.FirstOrDefault(i => SameName(i.Name, identifier)),
                snapshot.Clients.FirstOrDefault(c => SameName(c.Name, identifier)));

            return route is null
                ? Result<HeappeRoute, Error>.Failure(Errors.HeappeInstanceNotFound(identifier), State.NotFound)
                : Result<HeappeRoute, Error>.Success(route, State.Ok);
        }
        catch (Exception ex) when (HeappeTransportFailure.Is(ex, ct))
        {
            return Result<HeappeRoute, Error>.Failure(Errors.NexusUnavailable(ex), State.Error);
        }
    }

    public ValueTask InvalidateAsync(CancellationToken ct) => cache.RemoveAsync(SnapshotKey, ct);

    private async ValueTask<NexusDirectorySnapshot> CurrentSnapshotAsync(CancellationToken ct)
    {
        var snapshot = await LoadSnapshotAsync(ct);
        if (timeProvider.GetUtcNow() - snapshot.FetchedAt < Nexus.CacheMapTtl)
        {
            return snapshot;
        }

        await cache.RemoveAsync(SnapshotKey, ct);
        return await LoadSnapshotAsync(ct);
    }

    private ValueTask<NexusDirectorySnapshot> LoadSnapshotAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(
            SnapshotKey,
            registry,
            async (client, token) =>
            {
                var instances = client.GetInstancesAsync(token);
                var clients = client.GetClientsAsync(token);
                await Task.WhenAll(instances, clients);
                return new NexusDirectorySnapshot(await instances, await clients, timeProvider.GetUtcNow());
            },
            new HybridCacheEntryOptions { Expiration = Nexus.CacheMapTtl },
            cancellationToken: ct);

    // Nexus names an instance and its client the same way; the identifier is normalised to lower case, Nexus need not be.
    private static bool SameName(string name, HeappeIdentifier identifier) =>
        string.Equals(name, identifier.Value, StringComparison.OrdinalIgnoreCase);
}