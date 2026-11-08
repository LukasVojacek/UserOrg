using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.ServiceDefaults.Http;

namespace UserOrg.ServiceDefaults.Tests.Http;

internal static class TrustedProxyHost
{
    public const string ProbePath = "/trusted-proxy-probe";

    public static async Task<WebApplication> StartAsync(
        string environmentName,
        IDictionary<string, string?> settings,
        ILoggerProvider? loggerProvider = null,
        RequestDelegate? terminal = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ApplicationName = typeof(TrustedProxyHost).Assembly.GetName().Name
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        if (loggerProvider is not null)
        {
            builder.Logging.AddProvider(loggerProvider);
        }

        builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);

        var app = builder.Build();
        app.UseTrustedProxyForwardedHeaders();
        app.Run(terminal ?? CompleteRequest);
        await app.StartAsync();
        return app;
    }

    public static async Task<ObservedRequest> SendAsync(
        WebApplication app,
        IPAddress peer,
        string? forwardedFor,
        string? forwardedProto = null)
    {
        var context = await app.GetTestServer().SendAsync(ctx =>
        {
            ctx.Connection.RemoteIpAddress = peer;
            ctx.Request.Path = ProbePath;
            if (forwardedFor is not null)
            {
                ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }

            if (forwardedProto is not null)
            {
                ctx.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            }
        });

        return new ObservedRequest(context.Connection.RemoteIpAddress, context.Request.Scheme);
    }

    private static Task CompleteRequest(HttpContext context) => Task.CompletedTask;
}

internal sealed record ObservedRequest(IPAddress? RemoteIpAddress, string Scheme);