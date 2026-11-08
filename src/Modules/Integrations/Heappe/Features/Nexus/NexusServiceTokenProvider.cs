using System.Net.Http.Json;

using Heappe.Features.Connection;

using Microsoft.Extensions.Options;

namespace Heappe.Features.Nexus;

/// <summary>
/// Holds UserOrg's NexusApi service-account token. A singleton on purpose: <see cref="IHttpClientFactory"/> recycles
/// the message handlers (and so <see cref="NexusServiceTokenHandler"/>), the cache must outlive them.
/// </summary>
internal sealed class NexusServiceTokenProvider(IHttpClientFactory httpClientFactory, IOptions<TokenExchangeOptions> options, TimeProvider timeProvider)
{
    public const string ClientName = "NexusAuth";
    public const string TokenPath = "api/v1/auth/token";

    // The token is dropped this long before NexusApi says it expires, so a request in flight never carries a dead one.
    private static readonly TimeSpan _expirySkew = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedToken? _cached;

    public async ValueTask<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && FreshToken() is { } fresh)
        {
            return fresh;
        }

        await _gate.WaitAsync(ct);
        try
        {
            return !forceRefresh && FreshToken() is { } issuedMeanwhile
                ? issuedMeanwhile
                : await IssueTokenAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? FreshToken() =>
        _cached is { } cached && timeProvider.GetUtcNow() < cached.ExpiresAt ? cached.Token : null;

    private async Task<string> IssueTokenAsync(CancellationToken ct)
    {
        var nexus = options.Value.Nexus;
        var credentials = new NexusTokenRequest(nexus.ClientId, nexus.ClientSecret);
        using var client = httpClientFactory.CreateClient(ClientName);
        using var response = await client.PostAsJsonAsync(new Uri(TokenPath, UriKind.Relative), credentials, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<NexusTokenResponse>(ct)
            ?? throw new HttpRequestException("NexusApi answered the token request without a body.");
        _cached = new CachedToken(token.AccessToken, timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn) - _expirySkew);
        return token.AccessToken;
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
}