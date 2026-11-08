using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.FeatureManagement;

using UserOrganizationManagement.API.Composition;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Guards the runtime half of the v1 gate: every route <c>MapV1CoreEndpoints</c> maps must carry the
/// <see cref="FeatureFlags.V1Api"/> metadata that <c>FeatureFlagMiddleware</c> turns into a 404 when the
/// flag is off. The startup half — whether the method is called at all — lives in <c>Program.cs</c>.
/// It stands up the real host composition — no infrastructure is contacted, the container is only built
/// and inspected — so it is tagged as an integration test per the repository convention.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class V1EndpointFlagGateTests
{
    [Fact]
    public void Should_MapV1Routes_When_MapV1CoreEndpointsCalled()
    {
        var routes = V1RouteEndpoints().Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty).ToList();

        Assert.Contains(routes, route => route.StartsWith("api/Service", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(routes, route => route.StartsWith("api/Public", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_StampV1ApiFlagMetadata_On_EveryMappedV1Route()
    {
        var endpoints = V1RouteEndpoints();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.Equal(
            FeatureFlags.V1Api,
            endpoint.Metadata.GetMetadata<EnabledByFeatureFlagMetadata>()?.FeatureFlagName));
    }

    // Read the endpoints off the app's route-builder data sources: the DI-resolved composite
    // EndpointDataSource is only populated once the request pipeline is built (on Run).
    private static List<RouteEndpoint> V1RouteEndpoints()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Dummy values only — the test never opens a connection, contacts Vault, or starts the host.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthenticationConfiguration:MetadataAddress"] = "https://composition-test.invalid",
            ["MailConfiguration:MailSenderUrl"] = "https://composition-test.invalid",
            ["ConnectionStrings:UserOrgDBContext"] = "Server=(local);Database=CompositionTest;Trusted_Connection=True;",
            ["ConnectionStrings:ForgeDBContext"] = "Server=(local);Database=CompositionTest;Trusted_Connection=True;",
            ["ConnectionStrings:WorkflowDBContext"] = "Server=(local);Database=CompositionTest;Trusted_Connection=True;",
            ["DefaultAdminRoleName"] = "SuperAdmin",
        });

        builder.AddHostInfrastructure();
        builder.AddV1Core();
        builder.AddV2Core(isV2ApiEnabled: false);

        using var app = builder.Build();
        app.MapV1CoreEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }
}
