using System.Net;
using System.Text;

using IntegrationTests.AuthMatrix;

namespace IntegrationTests.Support;

public sealed class GatewayReadinessTests
{
    [Fact]
    public async Task Should_WaitForTheRouteSurface_When_GatewayAnswersBeforeTheSurfaceLoads()
    {
        var gateway = new StartingGateway(requestsBeforeSurfaceLoads: 3);
        using var client = new HttpClient(gateway) { BaseAddress = new Uri("http://gateway.test") };

        var ready = await GatewayReadiness.WaitAsync(
            client,
            TimeSpan.FromMinutes(1),
            TimeSpan.Zero,
            () => { },
            TestContext.Current.CancellationToken);

        Assert.True(ready);
        Assert.True(gateway.SurfaceLoaded, "Readiness returned while the route-existence surface was still empty.");
    }

    private sealed class StartingGateway(int requestsBeforeSurfaceLoads) : HttpMessageHandler
    {
        private int _served;

        public bool SurfaceLoaded => _served >= requestsBeforeSurfaceLoads;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var surfaceLoaded = SurfaceLoaded;
            _served++;

            if (request.RequestUri!.AbsolutePath == "/api/version")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"Version":"1.0.0"}"""));

            return Task.FromResult(surfaceLoaded
                ? Json(HttpStatusCode.NotFound, $$"""{"status":404,"code":"{{GatewayRouteNotFound.Code}}"}""")
                : Json(HttpStatusCode.Unauthorized, """{"status":401,"code":"auth.unauthorized"}"""));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };
    }
}