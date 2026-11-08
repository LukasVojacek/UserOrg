using System.Text.Json.Serialization;

using Core.Composition;
using Core.DataAccess.ResourceRequests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Email;

using WorkflowEngine.Core.DependencyInjection;

namespace Core.ArchitectureTests;

/// <summary>
/// <c>GET v2/AuthenticationTypes</c> served through the real Core composition: the list is the configured
/// <c>Heappe:ClusterAuthTypes:AllowedAuthenticationTypeNames</c> allowlist, not a live HEAppE call.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuthenticationTypesEndpointTests : IAsyncDisposable
{
    private readonly WebApplication _app;

    public AuthenticationTypesEndpointTests()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WorkflowDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
            ["ConnectionStrings:UserOrgDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
            ["Heappe:ClusterAuthTypes:AllowedAuthenticationTypeNames:0"] = "SshCertificate",
            ["Heappe:ClusterAuthTypes:AllowedAuthenticationTypeNames:1"] = "ConfiguredOnlyInThisTest",
        });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<IEmailSender, NoopEmailSender>();
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.AddResourceRequestsPersistence();
        builder.Services.AddCore(builder.Configuration);
        builder.AddWorkflowEngine();

        _app = builder.Build();
        _app.MapCoreEndpoints();
    }

    [Fact]
    public async Task Should_ListTheConfiguredAuthenticationTypesByName_When_Requested()
    {
        var endpoint = ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(e =>
                (e.RoutePattern.RawText ?? string.Empty).EndsWith("v2/AuthenticationTypes", StringComparison.OrdinalIgnoreCase)
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false));
        Assert.True(endpoint is not null, "No GET endpoint is mapped at v2/AuthenticationTypes.");

        await using var scope = _app.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = "GET";
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/AuthenticationTypes";
        http.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(http);

        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal("""[{"Name":"SshCertificate"},{"Name":"ConfiguredOnlyInThisTest"}]""", body);
        Assert.Equal("2", http.Response.Headers["X-Total-Count"]);
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
