using System.Net;
using System.Text;

using Heappe.Contracts;
using Heappe.Features.KillSwitch;
using Heappe.Features.VersionResolver;
using Heappe.Helpers.Connection;
using Heappe.Persistence;

using Lukdrasil.Error;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

// Guards the cause-code taxonomy (ADR 0074): a failed HEAppE call stamps a stable, URL-free Error.Code that
// Core surfaces in the TemplatesInfo failure response. Here we pin the HTTP-status -> cause mapping at the
// point the status is actually known (SendRequestAsync), exercised through the public GetClusters call.
public sealed class HeappeServiceFailureCauseTests
{
    private static readonly Uri HeappeUri = new("http://localhost:8080");

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "unauthorized")]
    [InlineData(HttpStatusCode.InternalServerError, "upstream-error")]
    public async Task GetClusters_stamps_cause_code_matching_the_status(HttpStatusCode status, string expectedCause)
    {
        using var handler = new MockHttpMessageHandler(status, "denied");
        var service = CreateService(handler);

        var result = await service.GetClusters(ServiceConnection(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCause, result.Error.Code);
    }

    // The Nexus proxy answers its own failures as RFC 9457 problem+json; the type, not the bare status, names the
    // cause (a 502 from the proxy means the HEAppE instance is unreachable, not that HEAppE failed).
    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "urn:config-registry:heappe-upstream-unavailable", HeappeFailureCause.HeappeUpstreamUnavailable)]
    [InlineData(HttpStatusCode.NotFound, "urn:config-registry:heappe-instance-not-found", HeappeFailureCause.HeappeInstanceNotFound)]
    public async Task GetClusters_stamps_the_cause_named_by_a_nexus_problem_json_answer(HttpStatusCode status, string problemType, string expectedCause)
    {
        using var handler = new ResponseHandler(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""{"type":"{{problemType}}","title":"HEAppE instance unavailable","status":{{(int)status}}}""",
                Encoding.UTF8,
                "application/problem+json")
        });
        var service = CreateService(handler);

        var result = await service.GetClusters(ServiceConnection(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCause, result.Error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.InternalServerError, "boom")]
    public async Task GetClusters_disposes_the_response(HttpStatusCode status, string body)
    {
        var content = new DisposalTrackingContent(body);
        using var handler = new ResponseHandler(() => new HttpResponseMessage(status) { Content = content });
        var service = CreateService(handler);

        await service.GetClusters(ServiceConnection(), CancellationToken.None);

        Assert.True(content.Disposed, "the HEAppE response (and its content) must be disposed once read");
    }

    [Fact]
    public async Task GetClusters_lets_the_callers_cancellation_through()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "[]");
        var service = CreateService(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetClusters(ServiceConnection(), cancellation.Token).AsTask());
    }

    private static ServiceTokenHeappeConnectionInfo ServiceConnection() => new(HeappeUri, new HeappeIdentifier("heappe-mock"), "service-token");

    private static HeappeService CreateService(HttpMessageHandler handler)
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = HeappeUri });

        var authOptions = new Mock<IOptions<ClusterAuthTypeOptions>>();
        authOptions.Setup(x => x.Value).Returns(new ClusterAuthTypeOptions());

        return new HeappeService(
            new Mock<ILogger<HeappeService>>().Object,
            httpFactory.Object,
            new Mock<IFeatureManager>().Object,
            new Mock<IDbContextFactory<HeappeIntegrationDbContext>>().Object,
            new Mock<IKillSwitchStateService>().Object,
            new Mock<IHeappeVersionResolver>().Object,
            authOptions.Object);
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond());
        }
    }

    private sealed class DisposalTrackingContent(string body) : StringContent(body, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
