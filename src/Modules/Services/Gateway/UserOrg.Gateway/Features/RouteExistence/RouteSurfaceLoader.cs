using System.Collections.Immutable;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

using UserOrg.Shared.OpenApi;

namespace UserOrg.Gateway.Features.RouteExistence;

/// <summary>
/// Loads the route surface from the OpenAPI documents <see cref="ApiDocuments.Published"/> lists for the
/// gateway's <c>FeatureManagement:V1Api</c>, read once at startup, and reconciles it on an interval.
/// It never blocks startup and never publishes a partial surface: until every document has been read
/// once, the oracle stays empty and the gateway answers exactly as it did before ADR-0106.
/// </summary>
internal sealed partial class RouteSurfaceLoader(
    IHttpClientFactory httpClientFactory,
    RouteSurfaceProvider routeSurfaceProvider,
    IOptions<RouteExistenceOptions> options,
    IConfiguration configuration,
    ILogger<RouteSurfaceLoader> logger) : BackgroundService
{
    internal const string HttpClientName = "route-surface";

    // why: shared with Core's AddOpenApi registrations rather than copied, because a document Core registers and this list omits is never fetched and every route described only by it is answered 404.
    private readonly ImmutableArray<string> _documentNames = ApiDocuments.Published(configuration.GetValue($"FeatureManagement:{UserOrg.Shared.FeatureManagement.FeatureFlags.V1Api}", true));

    private byte[]? _loadedFingerprint;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var loaded = await TryLoadAsync(stoppingToken);
            var delay = loaded ? settings.RefreshInterval : settings.RetryInterval;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> TryLoadAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var documents = new List<ReadOnlyMemory<byte>>(_documentNames.Length);

        foreach (var documentName in _documentNames)
        {
            var documentPath = $"/api/{documentName}/openapi.json";

            try
            {
                using var response = await client.GetAsync(new Uri(documentPath, UriKind.Relative), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    LogDocumentRefused(logger, documentPath, (int)response.StatusCode);
                    return false;
                }

                documents.Add(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            }
            catch (HttpRequestException exception)
            {
                // why: the upstream API is routinely not listening yet while the gateway starts; the retry
                // loop is the handling, so this is an ordinary wait rather than a fault.
                LogUpstreamUnavailable(logger, documentPath, exception.Message);
                return false;
            }
            catch (TaskCanceledException)
            {
                LogUpstreamUnavailable(logger, documentPath, "the request timed out");
                return false;
            }
        }

        var fingerprint = Fingerprint(documents);
        if (_loadedFingerprint is not null && fingerprint.AsSpan().SequenceEqual(_loadedFingerprint))
        {
            return true;
        }

        try
        {
            routeSurfaceProvider.Publish(RouteSurface.FromOpenApiDocuments(documents));
        }
        catch (JsonException exception)
        {
            LogDocumentUnreadable(logger, exception);
            return false;
        }

        _loadedFingerprint = fingerprint;
        LogSurfaceLoaded(logger);
        return true;
    }

    // why: the published documents change only when Core is redeployed, so hashing them is what keeps a
    // refresh from recompiling every path template on an unchanged surface.
    private static byte[] Fingerprint(List<ReadOnlyMemory<byte>> documents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var document in documents)
        {
            hash.AppendData(document.Span);
        }

        return hash.GetHashAndReset();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Route-existence oracle disabled; unmatched paths keep their pre-ADR-0106 answer.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route surface loaded from the published OpenAPI documents.")]
    private static partial void LogSurfaceLoaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route surface not loaded yet: {DocumentPath} is unavailable ({Reason}); retrying.")]
    private static partial void LogUpstreamUnavailable(ILogger logger, string documentPath, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Route surface not loaded: {DocumentPath} answered {StatusCode}; the gateway keeps its previous surface.")]
    private static partial void LogDocumentRefused(ILogger logger, string documentPath, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Route surface not loaded: an OpenAPI document could not be parsed; the gateway keeps its previous surface.")]
    private static partial void LogDocumentUnreadable(ILogger logger, Exception exception);
}