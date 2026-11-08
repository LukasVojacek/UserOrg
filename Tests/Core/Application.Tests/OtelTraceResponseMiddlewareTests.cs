using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using UserOrganizationManagement.API.Middleware;

public sealed class OtelTraceResponseMiddlewareTests
{
    [Fact(DisplayName = "Should_Add_Trace_Headers_When_Activity_Present")]
    public async Task Should_Add_Trace_Headers_When_Activity_Present()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers.Clear();

        RequestDelegate next = (ctx) => Task.CompletedTask;
        var middleware = new OtelTraceResponseMiddleware(next);

        using var activity = new Activity("test-activity");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        try
        {
            await middleware.InvokeAsync(context);

            Assert.True(context.Response.Headers.ContainsKey("X-Trace-Id"));
            var traceId = context.Response.Headers["X-Trace-Id"].ToString();
            Assert.False(string.IsNullOrEmpty(traceId));

            Assert.True(context.Response.Headers.ContainsKey("traceparent") || context.Response.Headers.ContainsKey("Traceparent"));
        }
        finally
        {
            activity?.Stop();
        }
    }
}