using System.Net;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using UserOrg.Gateway.Features.HealthChecks;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the upstream probe: it GETs <c>/health</c> on the named client, a success status is
/// Healthy, any other status and any exception report the registration's failure status.
/// </summary>
public sealed class UserOrgApiHealthCheckTests
{
    [Fact]
    public async Task Should_ReportHealthy_When_TheUpstreamAnswersSuccess()
    {
        using var handler = new ProbeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await CheckAsync(handler, HealthStatus.Unhealthy);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("UserOrg API is healthy", result.Description);
        Assert.Equal("GET https://userorg.test/health", Assert.Single(handler.Requests));
        Assert.Equal([UserOrgApiHealthCheck.HttpClientName], handler.RequestedClients);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, HealthStatus.Unhealthy)]
    [InlineData(HttpStatusCode.NotFound, HealthStatus.Degraded)]
    public async Task Should_ReportTheFailureStatusWithTheCode_When_TheUpstreamAnswersAnError(HttpStatusCode statusCode, HealthStatus failureStatus)
    {
        using var handler = new ProbeHandler(_ => new HttpResponseMessage(statusCode));

        var result = await CheckAsync(handler, failureStatus);

        Assert.Equal(failureStatus, result.Status);
        Assert.Equal($"UserOrg API returned HTTP {(int)statusCode}", result.Description);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task Should_ReportTheFailureStatusWithTheException_When_TheUpstreamCannotBeReached(HealthStatus failureStatus)
    {
        var failure = new HttpRequestException("connection refused");
        using var handler = new ProbeHandler(_ => throw failure);

        var result = await CheckAsync(handler, failureStatus);

        Assert.Equal(failureStatus, result.Status);
        Assert.Equal("UserOrg API health check failed", result.Description);
        Assert.Same(failure, result.Exception);
    }

    private static Task<HealthCheckResult> CheckAsync(ProbeHandler handler, HealthStatus failureStatus)
    {
        var healthCheck = new UserOrgApiHealthCheck(handler);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("userorg-api", healthCheck, failureStatus, tags: null),
        };
        return healthCheck.CheckHealthAsync(context, TestContext.Current.CancellationToken);
    }

    private sealed class ProbeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];

        public List<string> RequestedClients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedClients.Add(name);
            return new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://userorg.test") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(respond(request));
        }
    }
}
