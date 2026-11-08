using System.Diagnostics;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Features.Diagnostics;

internal static class TracingExtensions
{
    public static void AddStandardTracing(this TransformBuilderContext transformBuilder)
    {
        // Ensure inbound request has traceparent; create one if missing
        transformBuilder.AddRequestTransform(async context =>
        {
            var headers = context.HttpContext.Request.Headers;
            if (!headers.ContainsKey("traceparent"))
            {
                // Read Activity.Current (per-request, set by ASP.NET Core/OTel middleware) without mutating it.
                // If null (sampler opted out), generate a fresh random traceparent instead of calling
                // .Start() which would mutate AsyncLocal and bleed into concurrent requests.
                string tp;
                if (Activity.Current is { } activity)
                {
                    tp = $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01";
                }
                else
                {
                    tp = $"00-{ActivityTraceId.CreateRandom().ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
                }

                context.ProxyRequest.Headers.TryAddWithoutValidation("traceparent", tp);
                context.HttpContext.Request.Headers["traceparent"] = tp;
            }

            await ValueTask.CompletedTask;
        });

        // Propagate W3C trace context to backend (traceparent, tracestate, baggage)
        transformBuilder.AddRequestTransform(async context =>
        {
            var headers = context.HttpContext.Request.Headers;
            if (headers.TryGetValue("traceparent", out var traceparent))
            {
                context.ProxyRequest.Headers.Remove("traceparent");
                context.ProxyRequest.Headers.TryAddWithoutValidation("traceparent", traceparent.ToString());
            }
            if (headers.TryGetValue("tracestate", out var tracestate))
            {
                context.ProxyRequest.Headers.Remove("tracestate");
                context.ProxyRequest.Headers.TryAddWithoutValidation("tracestate", tracestate.ToString());
            }
            if (headers.TryGetValue("baggage", out var baggage))
            {
                context.ProxyRequest.Headers.Remove("baggage");
                context.ProxyRequest.Headers.TryAddWithoutValidation("baggage", baggage.ToString());
            }

            await ValueTask.CompletedTask;
        });

        // Add Request-Id header for distributed tracing if not already present
        transformBuilder.AddRequestTransform(async context =>
        {
            if (!context.HttpContext.Request.Headers.ContainsKey("Request-Id"))
            {
                var requestId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.ProxyRequest.Headers.TryAddWithoutValidation("Request-Id", requestId);
            }

            await ValueTask.CompletedTask;
        });

        // Add X-Correlation-ID header for correlation across distributed services
        transformBuilder.AddRequestTransform(async context =>
        {
            if (!context.HttpContext.Request.Headers.ContainsKey("X-Correlation-ID"))
            {
                context.ProxyRequest.Headers.TryAddWithoutValidation(
                    "X-Correlation-ID",
                    context.HttpContext.TraceIdentifier);
            }

            await ValueTask.CompletedTask;
        });

        // Ensure response includes trace headers for clients (traceparent + X-Trace-Id)
        transformBuilder.AddResponseTransform(async context =>
        {
            UserOrg.Shared.Diagnostics.TracingHelper.EnsureResponseTraceHeaders(context.HttpContext);
            await ValueTask.CompletedTask;
        });
    }
}