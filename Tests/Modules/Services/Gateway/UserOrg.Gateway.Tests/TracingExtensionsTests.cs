using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Features.Diagnostics;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the trace headers <see cref="TracingExtensions.AddStandardTracing"/> puts on the proxied
/// request: a missing traceparent is taken from the current activity or generated (and written back onto
/// the inbound request), traceparent, tracestate and baggage are copied through, and Request-Id and
/// X-Correlation-ID are added only when the caller did not send them. The response gets traceparent and
/// X-Trace-Id.
/// </summary>
public sealed class TracingExtensionsTests
{
    private const string InboundTraceparent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string TraceIdentifier = "trace-identifier-1";

    [Fact]
    public async Task Should_ForwardTheInboundTraceContext_When_TheCallerSendsIt()
    {
        Activity.Current = null;
        var httpContext = Inbound(("traceparent", InboundTraceparent), ("tracestate", "vendor=1"), ("baggage", "user=42"));

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        Assert.Equal([InboundTraceparent], proxyRequest.Headers.GetValues("traceparent"));
        Assert.Equal(["vendor=1"], proxyRequest.Headers.GetValues("tracestate"));
        Assert.Equal(["user=42"], proxyRequest.Headers.GetValues("baggage"));
    }

    [Fact]
    public async Task Should_LeaveTraceStateAndBaggageOut_When_TheCallerSendsNeither()
    {
        Activity.Current = null;
        var httpContext = Inbound(("traceparent", InboundTraceparent));

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        Assert.False(proxyRequest.Headers.Contains("tracestate"));
        Assert.False(proxyRequest.Headers.Contains("baggage"));
    }

    [Fact]
    public async Task Should_DeriveTheTraceparentFromTheCurrentActivity_When_TheCallerSendsNone()
    {
        using var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();
        var httpContext = Inbound();

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        var expected = $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01";
        Assert.Equal([expected], proxyRequest.Headers.GetValues("traceparent"));
        Assert.Equal(expected, httpContext.Request.Headers["traceparent"].ToString());
    }

    [Fact]
    public async Task Should_GenerateARandomTraceparent_When_TheCallerSendsNoneAndNoActivityRuns()
    {
        Activity.Current = null;
        var httpContext = Inbound();

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        var generated = Assert.Single(proxyRequest.Headers.GetValues("traceparent"));
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-01$", generated);
        Assert.Equal(generated, httpContext.Request.Headers["traceparent"].ToString());
    }

    [Fact]
    public async Task Should_AddRequestIdAndCorrelationIdFromTheTraceIdentifier_When_NoActivityRunsAndTheCallerSendsNeither()
    {
        Activity.Current = null;
        var httpContext = Inbound(("traceparent", InboundTraceparent));

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        Assert.Equal([TraceIdentifier], proxyRequest.Headers.GetValues("Request-Id"));
        Assert.Equal([TraceIdentifier], proxyRequest.Headers.GetValues("X-Correlation-ID"));
    }

    [Fact]
    public async Task Should_UseTheActivityIdAsRequestId_When_AnActivityRuns()
    {
        using var activity = new Activity("request");
        activity.Start();
        var httpContext = Inbound(("traceparent", InboundTraceparent));

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        Assert.Equal([activity.Id], proxyRequest.Headers.GetValues("Request-Id"));
        Assert.Equal([TraceIdentifier], proxyRequest.Headers.GetValues("X-Correlation-ID"));
    }

    [Fact]
    public async Task Should_NotAddRequestIdOrCorrelationId_When_TheCallerSendsThem()
    {
        Activity.Current = null;
        var httpContext = Inbound(("traceparent", InboundTraceparent), ("Request-Id", "caller-request"), ("X-Correlation-ID", "caller-correlation"));

        var proxyRequest = await ApplyRequestTransformsAsync(httpContext);

        Assert.False(proxyRequest.Headers.Contains("Request-Id"));
        Assert.False(proxyRequest.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Should_WriteTheTraceHeadersOnTheResponse_When_TheResponseIsTransformed()
    {
        Activity.Current = null;
        var builderContext = new TransformBuilderContext();
        builderContext.AddStandardTracing();
        var httpContext = Inbound(("traceparent", InboundTraceparent));
        var responseContext = new ResponseTransformContext { HttpContext = httpContext };

        var transform = Assert.Single(builderContext.ResponseTransforms);
        await transform.ApplyAsync(responseContext);

        Assert.Equal(InboundTraceparent, httpContext.Response.Headers["traceparent"].ToString());
        Assert.Equal(TraceIdentifier, httpContext.Response.Headers["X-Trace-Id"].ToString());
    }

    private static DefaultHttpContext Inbound(params (string Name, string Value)[] headers)
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = TraceIdentifier };
        foreach (var (name, value) in headers)
        {
            httpContext.Request.Headers[name] = value;
        }

        return httpContext;
    }

    private static async Task<HttpRequestMessage> ApplyRequestTransformsAsync(HttpContext httpContext)
    {
        var builderContext = new TransformBuilderContext();
        builderContext.AddStandardTracing();
        var requestContext = new RequestTransformContext { HttpContext = httpContext, ProxyRequest = new HttpRequestMessage() };

        foreach (var transform in builderContext.RequestTransforms)
        {
            await transform.ApplyAsync(requestContext);
        }

        return requestContext.ProxyRequest;
    }
}
