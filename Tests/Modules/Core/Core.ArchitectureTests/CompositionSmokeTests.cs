using System.Reflection;

using Core.Composition;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Primitives;
using Core.Domain.Provider;
using Core.Domain.User;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Email;
using UserOrg.Shared.FeatureManagement;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Ports;

namespace Core.ArchitectureTests;

/// <summary>
/// Composition/DI smoke test (plan item H2). Exercises the REAL host composition slice for the
/// provider-admin resource workflow — <c>AddCore</c> + <c>AddWorkflowEngine</c> + <c>MapCoreEndpoints</c>,
/// the exact calls <c>src/Core/API/Program.cs</c> makes — and asserts the invariants whose absence caused
/// the original blockers:
/// <list type="bullet">
///   <item><see cref="IRequestApplicationService"/> resolves (missing <c>AddWorkflowEngine</c> broke it).</item>
///   <item><see cref="ICallbackPort"/> resolves (missing adapter → <c>RequestWorker</c> construction throws).</item>
///   <item>The three new v2 routes are mapped (unmapped routes → 404 at runtime).</item>
///   <item><c>WorkflowDbContext</c> is registered/reachable (unregistered context → no persistence).</item>
/// </list>
/// It stands up a real host (no infrastructure is contacted — a dummy connection string is provided and
/// nothing is executed), so it is tagged as an integration test per the repository convention.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompositionSmokeTests : IAsyncDisposable
{
    private readonly WebApplication _app;

    public CompositionSmokeTests()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Minimum config the composition reads. Dummy values only — the test never opens a DB
        // connection, contacts Vault, or runs the host; it just builds the container + endpoints.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WorkflowDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
            ["ConnectionStrings:UserOrgDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
        });

        // MapCoreEndpoints calls .RequireAuthorization() on the api/v2 group; enumerating the data
        // source runs that convention, which needs the authorization services registered.
        builder.Services.AddAuthorization();

        // Stub in place of the adapter registration (AddModules), which this test never composes.
        builder.Services.AddScoped<IEmailSender, NoopEmailSender>();

        // The real composition slice, wired exactly as V2CoreExtensions does it: resource-request projection
        // persistence, AddCore (which folds in the assign-resource flow registration), then WorkflowEngine.
        builder.AddResourceRequestsPersistence();
        builder.Services.AddCore(builder.Configuration);
        builder.AddWorkflowEngine();

        _app = builder.Build();
        _app.MapCoreEndpoints();
    }

    [Fact]
    public void Should_Resolve_RequestApplicationService_When_ComposedAsHost()
    {
        // The CreateResourceOnLocation endpoint injects this driving port; before AddWorkflowEngine was
        // wired it failed to resolve at request time.
        using var scope = _app.Services.CreateScope();

        var service = scope.ServiceProvider.GetService<IRequestApplicationService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void Should_Resolve_CallbackPort_When_ComposedAsHost()
    {
        // RequestWorker (a singleton hosted service) constructor-injects ICallbackPort; a missing
        // registration throws when the worker is constructed. AddCoreResourceWorkflow supplies the adapter.
        using var scope = _app.Services.CreateScope();

        var callbackPort = scope.ServiceProvider.GetService<ICallbackPort>();

        Assert.NotNull(callbackPort);
    }

    [Fact]
    public void Should_Register_WorkflowDbContextFactory_When_ComposedAsHost()
    {
        // WorkflowDbContext is internal to WorkflowEngine.Core (this test project is not granted
        // InternalsVisibleTo), so the type is obtained by reflection off the public ICallbackPort's
        // assembly and the pooled IDbContextFactory<WorkflowDbContext> registration is resolved.
        var dbContextType = typeof(ICallbackPort).Assembly
            .GetType("WorkflowEngine.Core.Persistence.WorkflowDbContext");
        Assert.NotNull(dbContextType);

        var factoryType = typeof(IDbContextFactory<>).MakeGenericType(dbContextType);

        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetService(factoryType);

        Assert.NotNull(factory);
    }

    [Fact]
    public void Should_Register_AssignResourceFlowDefinition_When_ComposedAsHost()
    {
        // AddCore's AddFlow<AssignResourceFlow> compiles + validates the flow graph and registers its
        // IFlowDefinition (plus the synthesized IRequestStrategy and every step handler). Resolving the
        // definition proves the flow is wired and its DSL graph compiled without error.
        var definitions = _app.Services.GetServices<IFlowDefinition>().ToList();

        Assert.Contains(definitions, d => d.GetType().Name == "AssignResourceFlow");
    }

    [Fact]
    public void Should_Resolve_ProjectResourceActivatedEventHandler_When_ComposedAsHost()
    {
        // Creating a resource raises ProjectResourceActivatedEvent; the Core-side handler that resumes
        // parked requests must be registered (mirrors ProjectResourceSyncHandler) and constructible from its
        // resource-request query + workflow client dependencies.
        using var scope = _app.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler<ProjectResourceActivatedEvent>>();

        Assert.Contains(handlers, h => h.GetType().Name == "ProjectResourceActivatedEventHandler");
    }

    [Fact]
    public void Should_Resolve_UserInfoCacheEvictionHandler_When_UserEmailUpdatedEventComposed()
    {
        // UserEmailUpdatedEvent is IUserScopedEvent and its e-mail appears in the output-cached
        // UserInfoResponse (review B03); without this registration an e-mail change serves stale
        // cached UserInfo until the policy TTL, mirroring every other user-scoped cache-eviction handler.
        using var scope = _app.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler<UserEmailUpdatedEvent>>();

        Assert.Contains(handlers, h => h.GetType().Name.StartsWith("UserInfoCacheEvictionHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_Resolve_ResourceRequestProjectionStore_When_ComposedAsHost()
    {
        // The assign-resource flow's step handlers write through this projection store; AddCore registers it.
        // A missing registration would break every flow transition at runtime.
        using var scope = _app.Services.CreateScope();

        var store = scope.ServiceProvider.GetService<IResourceRequestProjectionStore>();

        Assert.NotNull(store);
    }

    [Theory]
    [InlineData("ProviderAdmin/Locations")]
    [InlineData("ProviderAdmin/Providers")]
    [InlineData("ProviderAdmin/Providers/{providerName}/Resources")]
    [InlineData("v2/AllocationUnitTypes")]
    [InlineData("v2/AuthenticationTypes")]
    [InlineData("ResourceRequests/{requestId}")]
    [InlineData("Projects/{projectShortName}/ResourceRequests")]
    [InlineData("Public/ResourceRequestActions/{token}")]
    public void Should_Map_NewV2Route_When_MapCoreEndpointsCalled(string routeFragment)
    {
        Assert.Contains(MappedRoutes(), r => r.Contains(routeFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_CarryV2ApiKillSwitchMetadata_When_MapCoreEndpointsCalled()
    {
        // The host only maps this group when V2Api was on at boot; the group convention is what lets a
        // later flip to off 404 the routes (FeatureFlagMiddleware) without restarting the process.
        var endpoints = ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
            Assert.Equal(
                FeatureFlags.V2Api,
                endpoint.Metadata.GetMetadata<EnabledByFeatureFlagMetadata>()?.FeatureFlagName));
    }

    [Fact]
    public void Should_NotMap_ResourceDraftRoute_When_MapCoreEndpointsCalled()
    {
        // The Draft catalog concept was removed (DEC-2) — the intake route must stay gone, not quietly reappear.
        Assert.DoesNotContain(MappedRoutes(), r => r.Contains("resource-drafts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_NotMap_EnumsAllocationUnitTypesRoute_When_MapCoreEndpointsCalled()
    {
        Assert.DoesNotContain(MappedRoutes(), r => r.Contains("enums/allocation-unit-types", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("v2/Locations", "locationTypeId")]
    [InlineData("v2/Providers/{providerName}/Resources", "locationTypeId")]
    [InlineData("ProviderAdmin/Providers/{providerName}/Resources", "locationTypeId")]
    [InlineData("v2/Providers", "countryId")]
    [InlineData("v2/Projects", "domainId")]
    public async Task Should_RejectRemovedQueryParameter_When_ListRouteCalledWithIt(string route, string removed)
    {
        var endpoint = Assert.Single(
            ((IEndpointRouteBuilder)_app).DataSources.SelectMany(ds => ds.Endpoints).OfType<RouteEndpoint>(),
            e => (e.RoutePattern.RawText ?? string.Empty).EndsWith(route, StringComparison.OrdinalIgnoreCase)
                && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
        var filter = Assert.Single(endpoint.Metadata.OfType<IEndpointFilter>());
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString($"?{removed}=1");

        var result = await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(http), _ => ValueTask.FromResult<object?>(null));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    private List<string> MappedRoutes() =>
        [.. ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)];

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
