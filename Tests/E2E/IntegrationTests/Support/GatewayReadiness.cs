using IntegrationTests.AuthMatrix;

namespace IntegrationTests.Support;

/// <summary>
/// Decides when the topology's Gateway is ready for the suite: it answers <c>/api/version</c>, and its
/// route-existence oracle (ADR-0106) has loaded, shown by the oracle's own 404 on an unserved v2 path.
/// The Gateway serves traffic before that first load by design and answers such a path 401 or 403 until then.
/// </summary>
internal static class GatewayReadiness
{
    internal const string UnservedPath = "/api/v2/there-is-no-such-route";

    /// <summary>
    /// Polls <paramref name="client"/> every <paramref name="interval"/> until the Gateway is ready, calling
    /// <paramref name="ensureAlive"/> before each poll. Returns false if <paramref name="timeout"/> elapses first.
    /// </summary>
    public static async Task<bool> WaitAsync(
        HttpClient client,
        TimeSpan timeout,
        TimeSpan interval,
        Action ensureAlive,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ensureAlive();

            if (await IsReadyAsync(client, cancellationToken))
                return true;

            await Task.Delay(interval, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsReadyAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var version = await TryGetAsync(client, "/api/version", cancellationToken);
        if (version is null || (int)version.StatusCode >= 500)
            return false;

        using var unserved = await TryGetAsync(client, UnservedPath, cancellationToken);
        return unserved is not null
            && GatewayRouteNotFound.Matches((int)unserved.StatusCode, await unserved.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task<HttpResponseMessage?> TryGetAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}