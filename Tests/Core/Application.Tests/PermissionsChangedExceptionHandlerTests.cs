using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Http;

using UserOrganizationManagement.API;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The API's own exception pipeline (<c>UseExceptionHandler</c> with <see cref="GlobalExceptionHandler"/>) clears
/// the response headers before it writes the 500. A permissions-changed header appended before the throw must
/// still reach the gateway on that 500, so an audit write failing after the commit cannot leave a stale snapshot.
/// </summary>
public sealed class PermissionsChangedExceptionHandlerTests
{
    private const string ChangedHeader = "X-UserOrg-Permissions-Changed";
    private const string CapabilityHeader = "X-UserOrg-Gateway-Capabilities";

    private static async Task<WebApplication> CreateTestAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "Application.Tests"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapPost("/committed-then-audit-failed", (HttpContext context) =>
        {
            context.Response.Headers["X-Before-Throw"] = "cleared";
            PermissionsChangedHeader.Append(context, ["user-a", "user-b"]);
            throw new InvalidOperationException("audit sink unavailable");
        });

        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> PostAsync(WebApplication app, bool withCapability)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/committed-then-audit-failed");
        if (withCapability)
            request.Headers.TryAddWithoutValidation(CapabilityHeader, "permissions-changed");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_CarryTheHeaderOnThe500_When_TheEndpointThrowsAfterAppending()
    {
        await using var app = await CreateTestAppAsync();

        using var response = await PostAsync(app, withCapability: true);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Before-Throw"));
        Assert.True(response.Headers.TryGetValues(ChangedHeader, out var values));
        var value = Assert.Single(values);
        Assert.Equal(["user-a", "user-b"], value.Split(',').Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Should_SendNothingOnThe500_When_TheGatewayDoesNotAdvertiseTheCapability()
    {
        await using var app = await CreateTestAppAsync();

        using var response = await PostAsync(app, withCapability: false);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains(ChangedHeader));
    }
}