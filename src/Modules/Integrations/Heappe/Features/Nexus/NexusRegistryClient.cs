using System.Net.Http.Json;

namespace Heappe.Features.Nexus;

/// <summary>Typed client of the NexusApi registry; every request carries the service-account token (see <see cref="NexusServiceTokenHandler"/>).</summary>
internal sealed class NexusRegistryClient(HttpClient httpClient)
{
    public const string InstancesPath = "api/v1/heappe";
    public const string ClientsPath = "api/v1/clients";

    public Task<IReadOnlyList<NexusHeappeInstance>> GetInstancesAsync(CancellationToken ct) =>
        GetListAsync<NexusHeappeInstance>(InstancesPath, ct);

    public Task<IReadOnlyList<NexusKeycloakClient>> GetClientsAsync(CancellationToken ct) =>
        GetListAsync<NexusKeycloakClient>(ClientsPath, ct);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(new Uri(path, UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(ct) ?? [];
    }
}