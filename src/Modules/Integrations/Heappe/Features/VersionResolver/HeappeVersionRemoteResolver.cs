using System.Text.Json;

using Heappe.Contracts;
using Heappe.Features.Nexus;

using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Heappe.Features.VersionResolver;

/// <summary>
/// Answers the version and the liveness of a HEAppE instance from its <c>heappe/Health</c> endpoint behind the Nexus
/// proxy. The route comes from the directory, not from the connection factory: the factory's user authentication
/// (<see cref="HeappeService"/>) depends on this resolver, so going through the factory would close a DI cycle.
/// </summary>
internal sealed class HeappeVersionRemoteResolver(
    HeappeVersionDatabaseResolver heappeVersionDatabaseResolver,
    IHttpClientFactory httpClientFactory,
    IHeappeInstanceDirectory directory,
    ILogger<HeappeVersionRemoteResolver> logger) : IHeappeVersionResolver, IHeappeLivenessProbe
{
    public const string heappeHealthEndoint = "heappe/Health";

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<Version> GetVersion(string projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken) =>
        ResolveVersionAsync(projectShortName, identifier, cancellationToken);

    public Task<Version> GetVersion(HeappeIdentifier identifier, CancellationToken cancellationToken) =>
        ResolveVersionAsync(null, identifier, cancellationToken);

    public async Task<bool> IsInstanceAliveAsync(HeappeIdentifier identifier, CancellationToken cancellationToken)
    {
        try
        {
            // Unlike ResolveVersionAsync, a failure here does NOT fall back to the DB — a liveness gate that answered
            // "alive" from the DB would defeat its purpose (the data calls would still hang on the dead host).
            return (await ProbeAsync(identifier, cancellationToken)).Alive;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller disconnected — propagate, not a liveness verdict.
            throw;
        }
        catch (Exception ex)
        {
            // Probe timeout or connection error — the instance is unreachable for our purposes.
            logger.LogWarning(ex, "Heappe health probe failed for {Identifier}; treating instance as unavailable", identifier);
            return false;
        }
    }

    private async Task<Version> ResolveVersionAsync(string? projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken)
    {
        try
        {
            var probe = await ProbeAsync(identifier, cancellationToken);
            if (probe.Version is { } version)
            {
                return version;
            }
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Failed to read the Heappe health endpoint of {Identifier}", identifier);
        }

        logger.LogInformation("Falling back to database resolver for Heappe version of {Identifier}", identifier);
        // Fallback to database resolver when anything goes wrong or version could not be extracted.
        return projectShortName is not null
            ? await heappeVersionDatabaseResolver.GetVersion(projectShortName, identifier, cancellationToken)
            : await heappeVersionDatabaseResolver.GetVersion(identifier, cancellationToken);
    }

    private static bool IsCallerCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    // Dedicated health-probe client: short flat timeout, no retries, no shared circuit breaker, so a slow/dead
    // instance fails fast without affecting data calls.
    private async Task<HealthProbe> ProbeAsync(HeappeIdentifier identifier, CancellationToken cancellationToken)
    {
        var route = await directory.ResolveAsync(identifier, cancellationToken);
        if (!route.IsSuccess)
        {
            logger.LogWarning("No Nexus route for Heappe instance {Identifier}: {Cause}", identifier, route.Error.Code);
            return HealthProbe.Down;
        }

        using var client = httpClientFactory.CreateClient(HeappeIntegrationExtensions.HeappeHealthClientName);
        using var resp = await client.GetAsync(new Uri(route.Value.ProxyBaseAddress, heappeHealthEndoint), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Heappe health endpoint returned non-success status {StatusCode} for {Identifier}", resp.StatusCode, identifier);
            return HealthProbe.Down;
        }

        return new HealthProbe(true, await ReadVersionAsync(resp, cancellationToken));
    }

    private async Task<Version?> ReadVersionAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            var health = await JsonSerializer.DeserializeAsync<HealthBody>(stream, _jsonOptions, cancellationToken);
            return ParseVersion(health?.Version);
        }
        catch (JsonException jex)
        {
            logger.LogWarning(jex, "Failed parsing Heappe health JSON");
            return null;
        }
    }

    // HEAppE writes the version as "v6.4.0" or "6.4.0".
    private static Version? ParseVersion(string? raw) =>
        Version.TryParse(raw?.Trim().TrimStart('v', 'V'), out var parsed) ? parsed : null;

    private sealed record HealthBody(string? Version);

    private sealed record HealthProbe(bool Alive, Version? Version)
    {
        public static readonly HealthProbe Down = new(false, null);
    }
}