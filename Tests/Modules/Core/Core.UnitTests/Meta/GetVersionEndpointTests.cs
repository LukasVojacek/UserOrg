using Core.Features.Meta.GetVersion;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Meta;

public sealed class GetVersionEndpointTests
{
    private const string EnvVar = "APP_VERSION";

    [Fact]
    public void Should_ReturnEnvironmentVersion_When_AppVersionSet()
    {
        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, "2.5.0");

            var ok = Assert.IsType<Ok<GetVersionEndpoint.Response>>(GetVersionEndpoint.HandleAsync());
            Assert.Equal("2.5.0", ok.Value!.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }
    }

    [Fact]
    public void Should_ReturnDefaultVersion_When_AppVersionUnset()
    {
        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, null);

            var ok = Assert.IsType<Ok<GetVersionEndpoint.Response>>(GetVersionEndpoint.HandleAsync());
            Assert.Equal("0.0.0.1", ok.Value!.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }
    }

    [Fact]
    public async Task Should_SerializeAsJsonObject_When_Executed()
    {
        // Mirrors the app's JsonOptions (HostInfrastructureExtensions): PropertyNamingPolicy = null keeps
        // PascalCase, consistent with every other v2 response.
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = null);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        var original = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, "2.5.0");

            await GetVersionEndpoint.HandleAsync().ExecuteAsync(http);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, original);
        }

        Assert.StartsWith("application/json", http.Response.ContentType, StringComparison.Ordinal);
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal("{\"Version\":\"2.5.0\"}", body);
    }
}
