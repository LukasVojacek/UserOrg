using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Core.UnitTests.Shared;

/// <summary>
/// An <see cref="HttpContext"/> whose response records <c>OnStarting</c> callbacks and runs them when the test
/// starts the response, the way the server does. <see cref="FailAsync"/> first does what
/// <c>ExceptionHandlerMiddleware</c> does before it writes a 500: it clears the response headers.
/// </summary>
internal static class PermissionsChangedResponse
{
    internal const string HeaderName = "X-UserOrg-Permissions-Changed";
    internal const string CapabilityHeader = "X-UserOrg-Gateway-Capabilities";
    internal const string Capability = "permissions-changed";

    private sealed class StartableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];
        private bool _started;

        public override bool HasStarted => _started;

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        internal async Task StartAsync()
        {
            _started = true;
            for (var i = _onStarting.Count - 1; i >= 0; i--)
                await _onStarting[i].Callback(_onStarting[i].State);
        }
    }

    internal static DefaultHttpContext Create(bool withCapability = true)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(new StartableResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));

        var context = new DefaultHttpContext(features);
        if (withCapability)
            context.Request.Headers[CapabilityHeader] = Capability;
        return context;
    }

    internal static async Task<StringValues> StartAsync(HttpContext context)
    {
        await ((StartableResponseFeature)context.Features.Get<IHttpResponseFeature>()!).StartAsync();
        return context.Response.Headers[HeaderName];
    }

    internal static Task<StringValues> FailAsync(HttpContext context)
    {
        context.Response.Headers.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return StartAsync(context);
    }

    internal static IReadOnlyList<string> Ids(StringValues value)
    {
        Assert.Equal(1, value.Count);
        return [.. value[0]!.Split(',').Order(StringComparer.Ordinal)];
    }
}