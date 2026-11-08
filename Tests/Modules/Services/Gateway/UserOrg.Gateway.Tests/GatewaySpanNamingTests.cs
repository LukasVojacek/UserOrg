using System.Diagnostics;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OpenTelemetry.Instrumentation.AspNetCore;

using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Every proxied request enters through one <c>/{**catch-all}</c> forwarder, so without this hook every
/// gateway span is named identically and a trace cannot tell two endpoints apart.
/// </summary>
public sealed class GatewaySpanNamingTests
{
    private const string Template = "/api/Project/TemplatesInfo/{projectShortName}";

    [Fact]
    public void Should_NameTheSpanAfterTheTemplateTheRequestMatched()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/Project/TemplatesInfo/TESTPRJ";
        context.Items[GatewayTracing.RouteTemplateItemKey] = Template;

        using var activity = Enrich(context, "GET /{**catch-all}");

        Assert.Equal($"GET {Template}", activity.DisplayName);
        Assert.Equal(Template, activity.GetTagItem("http.route"));
    }

    /// <summary>
    /// The whole point of taking the template rather than the path. A span named after the path would be
    /// a distinct name per project short name, per id, per GUID - and span name is an indexed, grouped
    /// dimension in every tracing backend, so that is worse than the single name it replaces.
    /// </summary>
    [Theory]
    [InlineData("TESTPRJ")]
    [InlineData("d290f1ee-6c54-4b01-90e6-d701748f0851")]
    public void Should_NotPutTheRequestsOwnIdentifiersInTheSpanName(string identifier)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = $"/api/Project/TemplatesInfo/{identifier}";
        context.Items[GatewayTracing.RouteTemplateItemKey] = Template;

        using var activity = Enrich(context, "GET /{**catch-all}");

        Assert.DoesNotContain(identifier, activity.DisplayName, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Blazor pages, the JWKS endpoints and the health endpoints are served by the gateway itself and
    /// already carry a real route; they never reach the permission lookup, so they must be left alone.
    /// </summary>
    [Fact]
    public void Should_LeaveARequestThatMatchedNoTemplateAlone()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/health";

        using var activity = Enrich(context, "GET /health");

        Assert.Equal("GET /health", activity.DisplayName);
        Assert.Null(activity.GetTagItem("http.route"));
    }

    [Fact]
    public void Should_LeaveARequestWhoseTemplateIsEmptyAlone()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/whatever";
        context.Items[GatewayTracing.RouteTemplateItemKey] = string.Empty;

        using var activity = Enrich(context, "GET /{**catch-all}");

        Assert.Equal("GET /{**catch-all}", activity.DisplayName);
    }

    /// <summary>
    /// Runs the registered callback against a started Activity, exactly as the instrumentation does at the
    /// end of a request. The options come out of a real container so the test fails if the registration in
    /// <see cref="GatewayTracing.AddGatewaySpanNaming"/> stops taking effect, not only if its body changes.
    /// </summary>
    private static Activity Enrich(HttpContext context, string initialDisplayName)
    {
        var options = new ServiceCollection()
            .AddGatewaySpanNaming()
            .BuildServiceProvider()
            .GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>()
            .Value;

        Assert.True(
            options.EnrichWithHttpResponse is not null,
            "AddGatewaySpanNaming registered no EnrichWithHttpResponse callback, so every proxied span keeps the catch-all name");

        var activity = new Activity("test");
        activity.Start();
        activity.DisplayName = initialDisplayName;

        options.EnrichWithHttpResponse!(activity, context.Response);

        activity.Stop();

        return activity;
    }
}
