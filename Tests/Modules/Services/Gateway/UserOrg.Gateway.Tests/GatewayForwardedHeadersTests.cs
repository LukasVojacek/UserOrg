using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Startup;
using UserOrg.ServiceDefaults.Tests.Http;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

public sealed partial class GatewayForwardedHeadersTests
{
    private const string ForwardedFor = "X-Forwarded-For";
    private const string ForwardedProto = "X-Forwarded-Proto";
    private const string ForwardedHost = "X-Forwarded-Host";
    private const string ForwardedPrefix = "X-Forwarded-Prefix";
    private const string RealIp = "X-Real-IP";
    private const string ClientId = "X-ClientId";

    private const string GatewayHost = "gateway.example";
    private const string GatewayPathBase = "/userorg";
    private const string ClientForwardedFor = "198.51.100.1";
    private const string GatewayPeer = "10.0.0.9";

    private static readonly string[] ClientIdentityHeaders = [RealIp, ClientId];

    private static readonly IPAddress SpoofedLoopback = IPAddress.Parse("127.0.0.1");
    private static readonly IPAddress RealClient = IPAddress.Parse("203.0.113.50");
    private static readonly IPAddress Ingress = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress GatewayAddress = IPAddress.Parse("10.0.0.3");

    [Fact]
    public async Task Should_AppendThePeerToTheIncomingXForwardedFor_When_TheGatewayForwardsARequest()
    {
        var forwarded = await ForwardThroughCatchAllAsync();

        Assert.Equal($"{ClientForwardedFor}, {GatewayPeer}", forwarded.Header(ForwardedFor));
    }

    [Fact]
    public async Task Should_SetProtoHostAndPrefixFromTheGatewayRequest_When_TheClientSentItsOwnForwardedHeaders()
    {
        var forwarded = await ForwardThroughCatchAllAsync();

        Assert.Equal(StatusCodes.Status401Unauthorized, forwarded.StatusCode);
        Assert.Equal("https", forwarded.Header(ForwardedProto));
        Assert.Equal(GatewayHost, forwarded.Header(ForwardedHost));
        Assert.Equal(GatewayPathBase, forwarded.Header(ForwardedPrefix));
        Assert.DoesNotContain("evil.example", forwarded.Header(ForwardedHost) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_NotForwardClientSentRealIpOrClientId_When_TheGatewayForwardsARequest()
    {
        var forwarded = await ForwardThroughCatchAllAsync();

        Assert.True(
            forwarded.Header(ForwardedPrefix) == GatewayPathBase,
            "the chain must reach the X-Forwarded transforms, otherwise this test proves nothing");
        Assert.True(
            forwarded.Header(RealIp) is null,
            $"a client-sent {RealIp} reached the API as '{forwarded.Header(RealIp)}'; the gateway must strip it");
        Assert.True(
            forwarded.Header(ClientId) is null,
            $"a client-sent {ClientId} reached the API as '{forwarded.Header(ClientId)}'; the gateway must strip it");
    }

    [Fact]
    public async Task Should_StripClientSentRealIpAndClientIdBeforeTheXForwardedForTransform_When_TheChainIsBuilt()
    {
        var builderContext = new TransformBuilderContext();
        builderContext.ConfigureUserOrgForwarderTransforms();
        var transforms = builderContext.RequestTransforms.ToList();
        await using var services = ChainServices();

        var xForwardedForIndex = transforms.FindIndex(transform => transform is RequestHeaderXForwardedForTransform);
        Assert.True(xForwardedForIndex >= 0, "ConfigureUserOrgForwarderTransforms registers no RequestHeaderXForwardedForTransform");

        var removers = new List<(int Index, string Header)>();
        for (var index = 0; index < transforms.Count; index++)
        {
            foreach (var header in await ClientHeadersRemovedWhenAppliedAloneAsync(transforms[index], services))
            {
                removers.Add((index, header));
            }
        }

        foreach (var header in ClientIdentityHeaders)
        {
            Assert.True(
                removers.Exists(remover => remover.Header == header),
                $"no request transform in ConfigureUserOrgForwarderTransforms removes a client-sent {header} from the proxy request");
        }

        foreach (var (index, header) in removers)
        {
            Assert.True(
                index < xForwardedForIndex,
                $"the transform removing {header} sits at index {index}, after the X-Forwarded-For transform at index {xForwardedForIndex}");
        }
    }

    [Fact]
    public async Task Should_KeepASpoofedLoopbackOutOfRemoteIpAddress_When_AClientSendsXForwardedForThroughTheIngress()
    {
        var forwarded = new TaskCompletionSource<ForwardedHop>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var gateway = await TrustedProxyHost.StartAsync(
            "Production",
            new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = Ingress.ToString() },
            terminal: async context =>
            {
                try
                {
                    forwarded.SetResult(await ForwardInPlaceAsync(context));
                }
                catch (Exception ex)
                {
                    forwarded.TrySetException(ex);
                    throw;
                }
            });

        var atGateway = await TrustedProxyHost.SendAsync(gateway, Ingress, $"{SpoofedLoopback}, {RealClient}");
        var gatewayHop = await forwarded.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotNull(gatewayHop);
        Assert.Equal(RealClient, atGateway.RemoteIpAddress);
        Assert.Equal(RealClient, gatewayHop.RemoteIpAddress);
        Assert.NotNull(gatewayHop.XForwardedFor);
        Assert.Equal(RealClient.ToString(), gatewayHop.XForwardedFor.Split(',')[^1].Trim());

        await using var api = await TrustedProxyHost.StartAsync(
            "Production",
            new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = GatewayAddress.ToString() });

        var atApi = await TrustedProxyHost.SendAsync(api, GatewayAddress, gatewayHop.XForwardedFor);

        Assert.Equal(RealClient, atApi.RemoteIpAddress);
        Assert.NotEqual(SpoofedLoopback, atGateway.RemoteIpAddress);
        Assert.NotEqual(SpoofedLoopback, atApi.RemoteIpAddress);
    }

    [Fact]
    public void Should_UseTrustedProxyForwardedHeadersFirst_When_TheGatewayPipelineIsBuilt()
    {
        var source = GatewayProgramSource();
        const string buildCall = "var app = builder.Build();";
        var buildIndex = source.IndexOf(buildCall, StringComparison.Ordinal);
        Assert.True(buildIndex >= 0, $"Program.cs of the gateway no longer contains '{buildCall}'");

        var firstUse = MiddlewareUse().Match(source, buildIndex + buildCall.Length);

        Assert.True(firstUse.Success, "Program.cs of the gateway calls no app.Use...( after builder.Build()");
        Assert.True(
            firstUse.Groups["name"].Value == "UseTrustedProxyForwardedHeaders",
            $"the first middleware after builder.Build() is app.{firstUse.Groups["name"].Value}(, expected app.UseTrustedProxyForwardedHeaders(");
    }

    [GeneratedRegex(@"\bapp\.(?<name>Use\w+)\(")]
    private static partial Regex MiddlewareUse();

    private static DefaultHttpContext GatewayRequest(IServiceProvider services)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = CatchAllForwarderTransformHarness.AnonymousUser()
        };
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(GatewayPeer);
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString(GatewayHost);
        httpContext.Request.PathBase = GatewayPathBase;
        httpContext.Request.Path = RouteSurfaceFixture.ServedV2Path;
        httpContext.Request.Headers[ForwardedFor] = ClientForwardedFor;
        httpContext.Request.Headers[ForwardedHost] = "evil.example";
        httpContext.Request.Headers[ForwardedProto] = "http";
        httpContext.Request.Headers[RealIp] = "192.0.2.77";
        httpContext.Request.Headers[ClientId] = "spoofed-client";
        return httpContext;
    }

    private static async Task<ForwardedRequest> ForwardThroughCatchAllAsync()
    {
        await using var services = ChainServices();
        var httpContext = GatewayRequest(services);
        using var proxyRequest = await TransformAsync(httpContext, services);
        return new ForwardedRequest(
            httpContext.Response.StatusCode,
            proxyRequest.Headers.NonValidated.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<ForwardedHop> ForwardInPlaceAsync(HttpContext context)
    {
        await using var services = ChainServices();
        var hostServices = context.RequestServices;
        context.RequestServices = services;
        using var proxyRequest = await TransformAsync(context, services);
        context.RequestServices = hostServices;
        var forwardedFor = proxyRequest.Headers.NonValidated.TryGetValues(ForwardedFor, out var values) ? values.ToString() : null;
        return new ForwardedHop(context.Connection.RemoteIpAddress, forwardedFor);
    }

    private static async Task<HttpRequestMessage> TransformAsync(HttpContext httpContext, IServiceProvider services)
    {
        var transformer = services.GetRequiredService<ITransformBuilder>()
            .Create(context => context.ConfigureUserOrgForwarderTransforms());
        var proxyRequest = new HttpRequestMessage();
        await transformer.TransformRequestAsync(httpContext, proxyRequest, "http://upstream", TestContext.Current.CancellationToken);
        return proxyRequest;
    }

    private static async Task<IReadOnlyList<string>> ClientHeadersRemovedWhenAppliedAloneAsync(RequestTransform transform, IServiceProvider services)
    {
        var httpContext = GatewayRequest(services);
        using var proxyRequest = new HttpRequestMessage();
        foreach (var (name, value) in httpContext.Request.Headers)
        {
            proxyRequest.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)value);
        }

        Assert.True(proxyRequest.Headers.Contains(RealIp) && proxyRequest.Headers.Contains(ClientId), "the copied proxy request must start with both client headers");

        await transform.ApplyAsync(new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = proxyRequest,
            DestinationPrefix = "http://upstream",
            HeadersCopied = true
        });

        return [.. ClientIdentityHeaders.Where(header => !proxyRequest.Headers.Contains(header))];
    }

    private static ServiceProvider ChainServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton(RouteSurfaceFixture.ProviderOf(RouteSurfaceFixture.FullSurface()))
            .AddSingleton(Mock.Of<IInternalTokenProvider>())
            .AddSingleton(Mock.Of<IUserPermissionsCache>())
            .AddReverseProxy()
            .Services
            .BuildServiceProvider();

    private static string GatewayProgramSource() =>
        File.ReadAllText(Path.Combine(RepoRoot.Directory(), "src", "Modules", "Services", "Gateway", "UserOrg.Gateway", "Program.cs"));

    private sealed record ForwardedRequest(int StatusCode, IReadOnlyDictionary<string, string> Headers)
    {
        public string? Header(string name) => Headers.GetValueOrDefault(name);
    }

    private sealed record ForwardedHop(IPAddress? RemoteIpAddress, string? XForwardedFor);
}