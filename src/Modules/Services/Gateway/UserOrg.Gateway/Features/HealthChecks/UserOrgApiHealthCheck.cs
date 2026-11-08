using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UserOrg.Gateway.Features.HealthChecks;

/// <summary>
/// Checks the health of the upstream UserOrg API by calling its <c>/health</c> endpoint.
/// The request is authenticated with an internal shared token so the endpoint
/// is not publicly accessible without that token.
/// </summary>
internal sealed class UserOrgApiHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    /// <summary>Named <see cref="HttpClient"/> used for the upstream health probe.</summary>
    internal const string HttpClientName = "UserOrgHealthCheck";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync("/health", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("UserOrg API is healthy");
            }

            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"UserOrg API returned HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "UserOrg API health check failed",
                ex);
        }
    }
}