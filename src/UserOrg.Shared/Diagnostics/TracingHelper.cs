using System.Diagnostics;

using Microsoft.AspNetCore.Http;

namespace UserOrg.Shared.Diagnostics
{
    public static class TracingHelper
    {
        public static void EnsureResponseTraceHeaders(HttpContext httpCtx)
        {
            var activity = Activity.Current;
            var traceParentValue = activity?.Id ?? httpCtx.Request.Headers["traceparent"].FirstOrDefault() ?? httpCtx.TraceIdentifier;
            var traceIdValue = activity?.TraceId.ToHexString() ?? httpCtx.TraceIdentifier;
            try
            {
                httpCtx.Response.Headers["traceparent"] = traceParentValue;
                httpCtx.Response.Headers["X-Trace-Id"] = traceIdValue;
            }
            catch
            {
                // best-effort, ignore if headers already sent
            }
        }

        public static string GetOrCreateTraceParent(HttpContext httpCtx)
        {
            var activity = Activity.Current ?? new Activity("generated").Start();
            return activity.Id ?? $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01";
        }
    }
}