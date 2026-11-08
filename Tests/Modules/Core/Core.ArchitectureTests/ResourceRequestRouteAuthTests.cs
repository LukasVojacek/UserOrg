using Core.Composition;
using Core.DataAccess.ResourceRequests;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Email;

using WorkflowEngine.Core.DependencyInjection;

namespace Core.ArchitectureTests;

/// <summary>
/// B10 feature-level RBAC/composition verification (plan §Verification, ADR-0063/0064): the seven new v2 routes
/// are mapped through the REAL host composition with the expected authorization posture — the two public
/// action-link routes carry <see cref="IAllowAnonymous"/> (reachable without a token, ADR-0064 anti-leak), while
/// every project-scoped and provider route is left under the <c>api/v2</c> group's <see cref="IAuthorizeData"/>
/// (Owner/Manager and ProviderAdmin gating is enforced by the gateway's mirrored RBAC perm keys, not by an
/// endpoint attribute). This locks the anonymous-vs-authorized split so a future edit that drops
/// <c>AllowAnonymous</c> from a public route — or adds it to a scoped one — fails here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResourceRequestRouteAuthTests : IAsyncDisposable
{
    private readonly WebApplication _app;

    public ResourceRequestRouteAuthTests()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WorkflowDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
            ["ConnectionStrings:UserOrgDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
        });
        builder.Services.AddAuthorization();

        // Stub in place of the adapter registration (AddModules), which this test never composes.
        builder.Services.AddScoped<IEmailSender, NoopEmailSender>();

        builder.AddResourceRequestsPersistence();
        builder.Services.AddCore(builder.Configuration);
        builder.AddWorkflowEngine();

        _app = builder.Build();
        _app.MapCoreEndpoints();
    }

    private RouteEndpoint Endpoint(string routeFragment, string httpMethod) =>
        ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e =>
                (e.RoutePattern.RawText ?? string.Empty).EndsWith(routeFragment, StringComparison.OrdinalIgnoreCase)
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(httpMethod) ?? false));

    [Theory]
    [InlineData("Public/ResourceRequestActions/{token}", "GET")]
    [InlineData("Public/ResourceRequestActions/{token}", "POST")]
    public void Should_BeAnonymous_When_PublicActionLinkRoute(string routeFragment, string method)
    {
        var endpoint = Endpoint(routeFragment, method);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Theory]
    [InlineData("Projects/{projectShortName}/ResourceRequests", "POST")]         // 1 create
    [InlineData("Projects/{projectShortName}/ResourceRequests", "GET")]          // 2 list
    [InlineData("Projects/{projectShortName}/ResourceRequests/{slug}/Cancel", "PUT")]           // 5 cancel
    [InlineData("Projects/{projectShortName}/ResourceRequests/{slug}/ResendApproval", "POST")] // 6 resend
    public void Should_RequireAuthorization_When_ScopedRoute(string routeFragment, string method)
    {
        var endpoint = Endpoint(routeFragment, method);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
