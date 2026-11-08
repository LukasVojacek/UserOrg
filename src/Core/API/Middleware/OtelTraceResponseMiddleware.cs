namespace UserOrganizationManagement.API.Middleware;

internal class OtelTraceResponseMiddleware
{
    private readonly RequestDelegate _next;
    public OtelTraceResponseMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected before the response was complete — this is normal (HTTP 499).
            // Suppress the exception so it doesn't log as an unhandled server error.
            context.Response.StatusCode = 499;
        }
        catch (InvalidOperationException ex)
            when (context.RequestAborted.IsCancellationRequested
                  && ex.Message.Contains("The connection is closed", StringComparison.OrdinalIgnoreCase))
        {
            // Microsoft.Data.SqlClient throws InvalidOperationException (not OperationCanceledException)
            // when a SqlCommand is cancelled mid-execution and the connection transitions to Closed state.
            // This is a SqlClient quirk triggered by request cancellation — treat it as a 499.
            context.Response.StatusCode = 499;
        }
        finally
        {
            UserOrg.Shared.Diagnostics.TracingHelper.EnsureResponseTraceHeaders(context);
        }
    }
}