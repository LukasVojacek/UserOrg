using System.Net;
using System.Net.Http.Headers;

namespace Heappe.Features.Nexus;

/// <summary>Adds the NexusApi service-account bearer token to a request and, on a 401, refreshes it and retries once.</summary>
internal sealed class NexusServiceTokenHandler(NexusServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await SendWithTokenAsync(request, forceRefresh: false, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        return await SendWithTokenAsync(request, forceRefresh: true, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpRequestMessage request, bool forceRefresh, CancellationToken ct)
    {
        var token = await tokenProvider.GetTokenAsync(forceRefresh, ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}