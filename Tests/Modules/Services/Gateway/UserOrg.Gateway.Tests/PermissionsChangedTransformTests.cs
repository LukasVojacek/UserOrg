using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Startup;

using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

public sealed class PermissionsChangedTransformTests
{
    private const string ChangedHeader = "X-UserOrg-Permissions-Changed";
    private const string CapabilityHeader = "X-UserOrg-Gateway-Capabilities";
    private const string Capability = "permissions-changed";

    private sealed class Gateway : IAsyncDisposable
    {
        internal Mock<IUserPermissionsCache> Cache { get; } = new();

        internal CapturingLoggerProvider Logs { get; } = new();

        internal ServiceProvider Services { get; }

        internal Gateway()
        {
            Cache.Setup(c => c.InvalidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Cache.Setup(c => c.InvalidateAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var tokens = new Mock<IInternalTokenProvider>();
            tokens.Setup(t => t.GenerateServiceToServiceTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: [new Claim(JwtRegisteredClaimNames.Jti, "s2s-jti")])));

            Services = new ServiceCollection()
                .AddLogging(logging => logging.AddProvider(Logs))
                .AddSingleton(RouteSurfaceFixture.ProviderOf(RouteSurfaceFixture.FullSurface()))
                .AddSingleton(tokens.Object)
                .AddSingleton(Cache.Object)
                .AddReverseProxy()
                .Services
                .BuildServiceProvider();
        }

        internal IReadOnlyList<CapturedLogEntry> Warnings => [.. Logs.All.Where(entry => entry.Level == LogLevel.Warning && entry.Category == typeof(Program).FullName)];

        internal async Task<HttpRequestMessage> ForwardAsync(string? clientCapability = null)
        {
            var httpContext = Request();
            if (clientCapability is not null)
                httpContext.Request.Headers[CapabilityHeader] = clientCapability;

            var transformer = CatchAll();
            var proxyRequest = new HttpRequestMessage();
            await transformer.TransformRequestAsync(httpContext, proxyRequest, "http://upstream", TestContext.Current.CancellationToken);
            Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
            return proxyRequest;
        }

        internal async Task<HttpContext> RespondAsync(HttpStatusCode status, params string[] changedValues)
        {
            var httpContext = Request();
            httpContext.Response.StatusCode = (int)status;
            using var proxyResponse = new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };
            proxyResponse.Headers.TryAddWithoutValidation("X-Other", "kept");
            if (changedValues.Length > 0)
                proxyResponse.Headers.TryAddWithoutValidation(ChangedHeader, changedValues);

            await CatchAll().TransformResponseAsync(httpContext, proxyResponse, TestContext.Current.CancellationToken);
            return httpContext;
        }

        private HttpTransformer CatchAll() =>
            Services.GetRequiredService<ITransformBuilder>().Create(context => context.ConfigureUserOrgForwarderTransforms());

        private DefaultHttpContext Request()
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = Services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Constants.PublicAccessClaimType, "true")], "Test")),
            };
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("gateway.example");
            httpContext.Request.Path = RouteSurfaceFixture.ServedV2Path;
            return httpContext;
        }

        public async ValueTask DisposeAsync() => await Services.DisposeAsync();
    }

    private static string[] ProxyHeader(HttpRequestMessage request, string name) =>
        request.Headers.NonValidated.TryGetValues(name, out var values) ? [.. values] : [];

    [Fact]
    public async Task Should_AdvertiseTheCapability_When_TheCatchAllForwardsARequest()
    {
        await using var gateway = new Gateway();

        using var proxied = await gateway.ForwardAsync();

        Assert.Equal([Capability], ProxyHeader(proxied, CapabilityHeader));
    }

    [Theory]
    [InlineData("permissions-changed")]
    [InlineData("something-else")]
    [InlineData("permissions-changed, something-else")]
    public async Task Should_ReplaceAClientSentCapability_When_TheCatchAllForwardsARequest(string clientValue)
    {
        await using var gateway = new Gateway();

        using var proxied = await gateway.ForwardAsync(clientValue);

        Assert.Equal([Capability], ProxyHeader(proxied, CapabilityHeader));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Should_EvictEachNamedSnapshot_When_AnyStatusCarriesTheHeader(HttpStatusCode status)
    {
        await using var gateway = new Gateway();

        var response = await gateway.RespondAsync(status, "user-a,user-b");

        gateway.Cache.Verify(c => c.InvalidateAsync("user-a", It.IsAny<CancellationToken>()), Times.Once);
        gateway.Cache.Verify(c => c.InvalidateAsync("user-b", It.IsAny<CancellationToken>()), Times.Once);
        gateway.Cache.VerifyNoOtherCalls();
        Assert.False(response.Response.Headers.ContainsKey(ChangedHeader));
        Assert.Equal("kept", response.Response.Headers["X-Other"].ToString());
        Assert.Empty(gateway.Warnings);
    }

    [Fact]
    public async Task Should_EvictEverySnapshot_When_TheHeaderIsTheEvictAllMarker()
    {
        await using var gateway = new Gateway();

        var response = await gateway.RespondAsync(HttpStatusCode.NoContent, "*");

        gateway.Cache.Verify(c => c.InvalidateAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        gateway.Cache.VerifyNoOtherCalls();
        Assert.False(response.Response.Headers.ContainsKey(ChangedHeader));
    }

    [Theory]
    [InlineData("*,user-a")]
    [InlineData("user a")]
    [InlineData("user-a,user\u0001b")]
    public async Task Should_EvictNothingAndWarn_When_TheValueIsMalformed(string value)
    {
        await using var gateway = new Gateway();

        var response = await gateway.RespondAsync(HttpStatusCode.OK, value);

        gateway.Cache.VerifyNoOtherCalls();
        Assert.False(response.Response.Headers.ContainsKey(ChangedHeader));
        Assert.NotEmpty(gateway.Warnings);
    }

    [Fact]
    public async Task Should_EvictNothingAndWarn_When_TheHeaderCarriesTwoValues()
    {
        await using var gateway = new Gateway();

        var response = await gateway.RespondAsync(HttpStatusCode.OK, "user-a", "user-b");

        gateway.Cache.VerifyNoOtherCalls();
        Assert.False(response.Response.Headers.ContainsKey(ChangedHeader));
        Assert.NotEmpty(gateway.Warnings);
    }

    [Fact]
    public async Task Should_StripTheHeaderAndWarn_When_EvictionThrows()
    {
        await using var gateway = new Gateway();
        gateway.Cache.Setup(c => c.InvalidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache down"));

        var response = await gateway.RespondAsync(HttpStatusCode.OK, "user-a");

        Assert.False(response.Response.Headers.ContainsKey(ChangedHeader));
        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        var warning = Assert.Single(gateway.Warnings);
        Assert.Contains("user-a", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_StripTheHeaderAndWarn_When_EvictAllThrows()
    {
        await using var gateway = new Gateway();
        gateway.Cache.Setup(c => c.InvalidateAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache down"));

        var response = await gateway.RespondAsync(HttpStatusCode.OK, "*");

        Assert.False(response.Response.Headers.ContainsKey(ChangedHeader));
        Assert.Single(gateway.Warnings);
    }

    [Fact]
    public async Task Should_EvictNothing_When_TheResponseCarriesNoHeader()
    {
        await using var gateway = new Gateway();

        var response = await gateway.RespondAsync(HttpStatusCode.OK);

        gateway.Cache.VerifyNoOtherCalls();
        Assert.Equal("kept", response.Response.Headers["X-Other"].ToString());
        Assert.Empty(gateway.Warnings);
    }
}