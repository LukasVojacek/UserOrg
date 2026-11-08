using System.Net;
using System.Net.Http.Json;

using Heappe.Contracts.Entries.Sync;
using Heappe.Features.CommandTemplateSynchronization;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace Heappe.UnitTests.Features.CommandTemplateSynchronization;

/// <summary>
/// Phase 3 endpoint tests for <see cref="ProjectCommandTemplatesSyncEndpoint.MapCommandTemplateSyncEndpoint"/>
/// (plan-ready.md test plan items 5-7). Locks in the per-location contract B04 introduces:
/// <list type="bullet">
/// <item>partial success (>=1 location succeeded) returns <c>200 OK</c> with the full
/// <see cref="CommandTemplateSyncResult"/> body,</item>
/// <item>total failure (every location failed → top-level <see cref="ValidationResult{T}"/> Failure)
/// returns <c>400 BadRequest</c>,</item>
/// <item>pre-location failure (project/resource/allocation lookup) still returns <c>400 BadRequest</c>
/// with today's <c>{ success = false, error }</c> shape — a regression that must NOT change.</item>
/// </list>
/// </summary>
public sealed class ProjectCommandTemplatesSyncEndpointTests
{
    /// <summary>
    /// Builds, starts, and returns a minimal <see cref="WebApplication"/> backed by a
    /// <see cref="TestServer"/>, with the given sync service mock registered for DI.
    /// The caller is responsible for disposing it.
    /// </summary>
    private static async Task<WebApplication> CreateTestAppAsync(IProjectCommandTemplateSyncService syncService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "Heappe.UnitTests"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(syncService);

        var app = builder.Build();
        app.MapCommandTemplateSyncEndpoint();
        await app.StartAsync();
        return app;
    }

    [Fact(DisplayName = "Partial success (>=1 location succeeded) returns 200 OK with the per-location breakdown")]
    public async Task ReturnsOk_WithBreakdown_WhenAnyLocationSucceeds()
    {
        var partialResult = new CommandTemplateSyncResult(
        [
            new LocationSyncResultEntry("loc-ok", "AGG-OK", new Uri("http://heappe-a.test"), IsSuccess: true, Errors: []),
            new LocationSyncResultEntry("loc-bad", "AGG-BAD", new Uri("http://heappe-b.test"), IsSuccess: false,
                Errors: [new Error("ERR-CLUSTERS", ErrorSeverity.Error, "Failed to list HEAppE clusters")])
        ]);

        var syncServiceMock = new Mock<IProjectCommandTemplateSyncService>();
        syncServiceMock
            .Setup(s => s.SyncCommandTemplatesForResource(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult<CommandTemplateSyncResult>.Success(partialResult));

        await using var app = await CreateTestAppAsync(syncServiceMock.Object);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            "/CommandTemplate/Synchronization",
            new { projectShortName = "proj", resourceName = "res" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CommandTemplateSyncResult>();
        Assert.NotNull(body);
        Assert.True(body!.AnySucceeded);
        Assert.False(body.AllSucceeded);
        Assert.Equal(2, body.Locations.Count);

        var okEntry = Assert.Single(body.Locations, l => l.LocationName == "loc-ok");
        Assert.True(okEntry.IsSuccess);
        Assert.Equal("loc-ok", okEntry.LocationName);
        Assert.Empty(okEntry.Errors);

        var badEntry = Assert.Single(body.Locations, l => l.LocationName == "loc-bad");
        Assert.False(badEntry.IsSuccess);
        Assert.Equal("loc-bad", badEntry.LocationName);
        Assert.NotEmpty(badEntry.Errors);
    }

    [Fact(DisplayName = "Total failure (all locations failed → top-level Failure) returns 400 BadRequest")]
    public async Task ReturnsBadRequest_WhenEveryLocationFails()
    {
        // B03's service collapses "every attempted location failed" into a top-level
        // ValidationResult Failure (ProjectCommandTemplateSyncService: !syncResult.AnySucceeded
        // -> ValidationResult<CommandTemplateSyncResult>.Failure(errors)), so the mock mirrors that.
        var error = new ValidationError(new Error("ERR-ALL", ErrorSeverity.Error, "All locations failed"));

        var syncServiceMock = new Mock<IProjectCommandTemplateSyncService>();
        syncServiceMock
            .Setup(s => s.SyncCommandTemplatesForResource(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult<CommandTemplateSyncResult>.Failure(error));

        await using var app = await CreateTestAppAsync(syncServiceMock.Object);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            "/CommandTemplate/Synchronization",
            new { projectShortName = "proj", resourceName = "res" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("success", out var successProp));
        Assert.False(successProp.GetBoolean());
        Assert.True(json.TryGetProperty("error", out _));
    }

    [Fact(DisplayName = "Pre-location failure (unknown project) returns 400 BadRequest with { success = false, error } shape")]
    public async Task ReturnsBadRequest_WithSuccessFalseAndError_WhenPreLocationFailure()
    {
        var error = new ValidationError(new Error("ERR001", ErrorSeverity.Error, "Allocation not found"));

        var syncServiceMock = new Mock<IProjectCommandTemplateSyncService>();
        syncServiceMock
            .Setup(s => s.SyncCommandTemplatesForResource(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult<CommandTemplateSyncResult>.Failure(error));

        await using var app = await CreateTestAppAsync(syncServiceMock.Object);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            "/CommandTemplate/Synchronization",
            new { projectShortName = "proj", resourceName = "res" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(json.TryGetProperty("success", out var successProp));
        Assert.False(successProp.GetBoolean());
        Assert.True(json.TryGetProperty("error", out _));
    }
}
