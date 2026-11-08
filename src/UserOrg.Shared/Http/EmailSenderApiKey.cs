namespace UserOrg.Shared.Http;

/// <summary>
/// The API key header the emailsender service (2.4.0 and later) requires on every request.
/// </summary>
public static class EmailSenderApiKey
{
    /// <summary>The request header name the emailsender service reads the API key from.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Adds the <see cref="HeaderName"/> header to the client's default headers; a blank key adds nothing.
    /// </summary>
    public static void ApplyTo(HttpClient client, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        client.DefaultRequestHeaders.Add(HeaderName, apiKey);
    }
}