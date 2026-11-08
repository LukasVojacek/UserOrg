using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using UserOrg.ServiceDefaults;

namespace ServiceDefaults.Tests;

/// <summary>
/// Verifies the HTTP behaviour of <c>/alive</c> and <c>/health</c> as mapped by
/// <see cref="Extensions.MapDefaultEndpoints"/>.  Each test spins up an in-process
/// <see cref="TestServer"/> and tears it down immediately after.
/// </summary>
public sealed class HealthEndpointsTests
{

    /// <summary>
    /// Builds, starts, and returns a minimal <see cref="WebApplication"/> backed by a
    /// <see cref="TestServer"/>.  The caller is responsible for disposing it.
    /// </summary>
    private static async Task<WebApplication> CreateTestAppAsync(
        Action<IHealthChecksBuilder> configureChecks)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "ServiceDefaults.Tests"
        });

        builder.WebHost.UseTestServer();

        var checksBuilder = builder.Services.AddHealthChecks();
        configureChecks(checksBuilder);

        var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }

    [Fact(DisplayName = "Should_ReturnOk_And_PlainText_When_AliveEndpointCalled")]
    public async Task Should_ReturnOk_And_PlainText_When_AliveEndpointCalled()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]));

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact(DisplayName = "Should_ReturnOk_For_Alive_When_NonLiveCheckIsDegraded")]
    public async Task Should_ReturnOk_For_Alive_When_NonLiveCheckIsDegraded()
    {
        // Only the "live"-tagged check drives /alive — a degraded check without
        // the tag must not affect the liveness response.
        await using var app = await CreateTestAppAsync(checks =>
            checks
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck("degraded-dep", () => HealthCheckResult.Degraded("simulated degradation")));

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact(DisplayName = "Should_ReturnServiceUnavailable_For_Alive_When_SelfCheckFails")]
    public async Task Should_ReturnServiceUnavailable_For_Alive_When_SelfCheckFails()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks.AddCheck("self", () => HealthCheckResult.Unhealthy("process down"), tags: ["live"]));

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Unhealthy", body);
    }

    [Fact(DisplayName = "Should_ReturnOk_And_Json_When_HealthEndpointCalled_And_AllChecksPass")]
    public async Task Should_ReturnOk_And_Json_When_HealthEndpointCalled_And_AllChecksPass()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]));

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "Should_ReturnServiceUnavailable_When_HealthEndpointCalled_And_CriticalCheckFails")]
    public async Task Should_ReturnServiceUnavailable_When_HealthEndpointCalled_And_CriticalCheckFails()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck("critical", () => HealthCheckResult.Unhealthy("db down"), tags: ["db"]));

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact(DisplayName = "Should_HealthJson_ContainStatusAndTotalDuration")]
    public async Task Should_HealthJson_ContainStatusAndTotalDuration()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]));

        using var client = app.GetTestClient();
        var json = await (await client.GetAsync("/health").ConfigureAwait(false)).Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("status", out var statusProp));
        Assert.Equal("Healthy", statusProp.GetString());

        Assert.True(doc.RootElement.TryGetProperty("totalDuration", out _));
    }

    [Fact(DisplayName = "Should_HealthJson_ContainEntriesWithStatusAndDuration")]
    public async Task Should_HealthJson_ContainEntriesWithStatusAndDuration()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]));

        using var client = app.GetTestClient();
        var json = await (await client.GetAsync("/health").ConfigureAwait(false)).Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var entry = doc.RootElement.GetProperty("entries").GetProperty("self");

        Assert.True(entry.TryGetProperty("status", out var statusProp));
        Assert.Equal("Healthy", statusProp.GetString());
        Assert.True(entry.TryGetProperty("duration", out _));
    }

    [Fact(DisplayName = "Should_HealthJson_NotExposeInternalDetails")]
    public async Task Should_HealthJson_NotExposeInternalDetails()
    {
        // Even when a check provides a description, exception and tags the public
        // endpoint must not forward any of those fields.
        await using var app = await CreateTestAppAsync(checks =>
            checks.AddCheck(
                "probe",
                () => new HealthCheckResult(
                    HealthStatus.Healthy,
                    description: "internal description",
                    exception: new InvalidOperationException("secret error")),
                tags: ["internal-tag"]));

        using var client = app.GetTestClient();
        var json = await (await client.GetAsync("/health").ConfigureAwait(false)).Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("entries").GetProperty("probe");

        Assert.False(entry.TryGetProperty("description", out _), "description must not be exposed");
        Assert.False(entry.TryGetProperty("exception", out _), "exception must not be exposed");
        Assert.False(entry.TryGetProperty("tags", out _), "tags must not be exposed");
    }

    [Fact(DisplayName = "Should_HealthJson_ContainAllRegisteredChecks")]
    public async Task Should_HealthJson_ContainAllRegisteredChecks()
    {
        await using var app = await CreateTestAppAsync(checks =>
            checks
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck("dep-a", () => HealthCheckResult.Healthy())
                .AddCheck("dep-b", () => HealthCheckResult.Degraded()));

        using var client = app.GetTestClient();
        var json = await (await client.GetAsync("/health").ConfigureAwait(false)).Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");

        Assert.True(entries.TryGetProperty("self", out _));
        Assert.True(entries.TryGetProperty("dep-a", out _));
        Assert.True(entries.TryGetProperty("dep-b", out _));
    }
}
