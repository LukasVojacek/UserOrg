using System.Collections.Concurrent;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace UserOrg.Gateway.Tests.Logging;

internal sealed class StubUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<string> _requests;

    private StubUpstream(WebApplication app, ConcurrentQueue<string> requests)
    {
        _app = app;
        _requests = requests;
    }

    public string Address => _app.Urls.Single();

    public IReadOnlyList<string> Requests => [.. _requests];

    public static async Task<StubUpstream> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var requests = new ConcurrentQueue<string>();
        app.Run(context =>
        {
            requests.Enqueue($"{context.Request.Path}{context.Request.QueryString}");
            return Task.CompletedTask;
        });

        await app.StartAsync();
        return new StubUpstream(app, requests);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}