using System.Net.Http.Json;
using System.Text.Json;

namespace E2E.Fixtures;

/// <summary>
/// Extension helpers for HttpResponseMessage used by E2E tests.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Reads the response body as a JSON element without assuming a specific type.
    /// </summary>
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return payload;
    }
}
